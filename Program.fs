open Docker.DotNet
open System
open System.Threading
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection
open Giraffe
open ContainerStreaming
open Microsoft.Extensions.Configuration

type ContainerServiceError = ServiceError of string

type ContainerDto = Database.ContainerEntity

let unwrapServiceError (ServiceError e) = "ServiceError(" + e + ")"

type ContainerService() =
  member _.lock: SemaphoreSlim = new SemaphoreSlim(1)

  member cs.CreateContainer(container: ContainerDto) =
    async {
      let! acquired = (cs.lock.WaitAsync(100)) |> Async.AwaitTask

      if acquired then
        try
          let existing = Database.getDesiredStateByName container.Name

          match existing with
          | Ok x when x.Length > 0 -> return Error(ServiceError "Container already exists")
          | _ ->
            let result = Database.saveDesiredState container

            return
              match result with
              | Ok _ -> Ok container
              | Error e -> Error(ServiceError e.Message)

        finally
          cs.lock.Release() |> ignore
      else
        printfn "Failed to acquire lock"
        return Error(ServiceError "Failed to acquire lock")
    }

let containerCreateHandler =
  handleContext (fun ctx ->
    let cs = ctx.GetService<ContainerService>()

    let container =
      ctx.BindJsonAsync<ContainerDto>() |> Async.AwaitTask |> Async.RunSynchronously

    let result = container |> cs.CreateContainer |> Async.RunSynchronously

    match result with
    | Ok c -> ctx.WriteJsonAsync c
    | Error e ->
      ctx.SetStatusCode 500
      ctx.WriteJsonAsync(e |> unwrapServiceError))

let containerGetHandler =
  handleContext (fun ctx ->
    let result = Database.getDesiredState ()

    match result with
    | Ok state -> ctx.WriteJsonAsync state
    | Error e -> ctx.WriteJsonAsync e.Message)

let webApp =
  choose
    [ POST >=> choose [ route "/container" >=> containerCreateHandler ]
      GET >=> choose [ route "/container" >=> containerGetHandler ] ]

let configureApp (app: IApplicationBuilder) = app.UseGiraffe webApp

let configureServices (services: IServiceCollection) =
  services
    .AddGiraffe()
    .AddSingleton<ContainerService>()
    .AddSingleton<DockerClient>(fun sp ->
      let config =
        sp.GetRequiredService<IConfiguration>().GetValue<string>("DockerSocket")

      match config with
      | null -> failwith "DockerSocket configuration not found"
      | value -> (new DockerClientConfiguration(Uri(value))).CreateClient())
    .AddHostedService<ReconcileService>()
  |> ignore

let prerequisites (client: DockerClient) =
  task {
    try
      let info = System.Diagnostics.ProcessStartInfo()
      info.FileName <- "docker"
      info.Arguments <- "info --format json"
      info.RedirectStandardOutput <- true
      info.RedirectStandardError <- true

      let shell = System.Diagnostics.Process.Start(info)

      do! shell.WaitForExitAsync()

      let content = shell.StandardError.ReadToEnd()

      return
        match content with
        | _ when content.StartsWith("Cannot connect") -> Error "Docker is not running"
        | _ -> Ok()
    with ex ->
      return Error ex.Message
  }

[<EntryPoint>]
let main _ =
  task {
    let allowedRange = { From = Port 15000; To = Port 15999 }
    let ctks = new CancellationTokenSource()
    let builder = Host.CreateDefaultBuilder()

    let app =
      builder
        .ConfigureWebHostDefaults(fun webHostBuilder ->
          webHostBuilder.Configure(configureApp).ConfigureServices(configureServices)
          |> ignore)
        .Build()

    let client = app.Services.GetRequiredService<DockerClient>()
    let! res = prerequisites client

    match res with
    | Error e ->
      printfn "%s" e
      return 1
    | Ok() ->
      printfn "Docker found"
      do! initializeStateOfTheSystem client ctks.Token |> Async.AwaitTask

      listen_to_changes client ctks.Token

      do! app.RunAsync()
      do! ctks.CancelAsync()
      return 0
  }
  |> Async.AwaitTask
  |> Async.RunSynchronously
