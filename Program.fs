open Docker.DotNet
open System
open System.Threading
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection
open Giraffe
open ContainerStreaming

type ContainerServiceError = ServiceError of string

let unwrapServiceError (ServiceError e) = "ServiceError(" + e + ")"


type ContainerService() =
    member _.lock: SemaphoreSlim = new SemaphoreSlim(1)

    member cs.CreateContainer(container: Database.CreateContainerReq) =
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
                            | Ok x -> Ok container
                            | Error e -> Error(ServiceError e.Message)

                finally
                    printfn "Releasing lock"
                    let count = cs.lock.Release()

                    printfn "Released lock: %d" count
            else
                printfn "Failed to acquire lock"
                return Error(ServiceError "Failed to acquire lock")
        }


let client =
    (new DockerClientConfiguration(Uri("unix:///var/run/docker.sock")))
        .CreateClient()

let cs = ContainerService()

let containerCreateHandler =
    handleContext (fun ctx ->
        let container =
            ctx.BindJsonAsync<Database.CreateContainerReq>()
            |> Async.AwaitTask
            |> Async.RunSynchronously

        printfn "Creating container %A" container


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
        .AddHostedService<ReconcileService>(fun _ -> new ReconcileService(500, client))
    |> ignore

    services.AddSingleton<Json.ISerializer>(Json.Serializer(Json.Serializer.DefaultOptions))
    |> ignore

[<EntryPoint>]
let main _ =
    let ctks = new CancellationTokenSource()
    initState client ctks.Token |> Async.AwaitTask |> Async.RunSynchronously
    listen_to_changes client ctks.Token

    Host
        .CreateDefaultBuilder()
        .ConfigureWebHostDefaults(fun webHostBuilder ->
            webHostBuilder.Configure(configureApp).ConfigureServices(configureServices)
            |> ignore)
        .Build()
        .Run()

    ctks.Cancel()
    0
