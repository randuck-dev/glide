open Docker.DotNet
open System
open System.Threading
open Fumble
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection
open Giraffe

let sqliteConnectionString = "Data Source=containers.db"

module Database =
    type CreateContainerReq =
        { Image: string
          Name: string
          Command: string }

    type DesiredContainerState =
        { Id: string option
          Name: string
          Image: string
          Command: string }

    let getDesiredState =
        sqliteConnectionString
        |> Sql.connect
        |> Sql.query "SELECT * FROM DesiredContainerState"
        |> Sql.execute (fun read ->
            { Id = read.stringOrNone "id"
              Name = read.string "name"
              Image = read.string "image"
              Command = read.string "command" })

    let getDesiredStateByName name =
        sqliteConnectionString
        |> Sql.connect
        |> Sql.query "SELECT * FROM DesiredContainerState WHERE name = @name"
        |> Sql.parameters [ "@name", Sql.string name ]
        |> Sql.execute (fun read ->
            { Id = read.stringOrNone "id"
              Name = read.string "name"
              Image = read.string "image"
              Command = read.string "command" })

    let saveDesiredState (state: CreateContainerReq) =
        sqliteConnectionString
        |> Sql.connect
        |> Sql.query "INSERT INTO DesiredContainerState (name, image, command) VALUES (@name, @image, @command)"
        |> Sql.parameters
            [ "@name", Sql.string state.Name
              "@image", Sql.string state.Image
              "@command", Sql.string state.Command ]
        |> Sql.executeNonQuery

type ContainerServiceError = ServiceError of string

let unwrapServiceError (ServiceError e) = e


type ContainerService() =
    member _.lock: SemaphoreSlim = new SemaphoreSlim(1)

    member cs.CreateContainer(container: Database.CreateContainerReq) =
        async {
            let! acquired = (cs.lock.WaitAsync(100)) |> Async.AwaitTask

            if acquired then
                try
                    let existing = Database.getDesiredStateByName container.Name

                    match existing with
                    | Ok x -> return Error(ServiceError "Container already exists")
                    | _ ->
                        printfn "Creating container %s" container.Name
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

let ctks = new CancellationTokenSource()

let client =
    (new DockerClientConfiguration(Uri("unix:///var/run/docker.sock")))
        .CreateClient()

ContainerStreaming.listen_to_changes client ctks.Token
let cs = ContainerService()

let containerCreateHandler =
    handleContext (fun ctx ->
        let container: Database.CreateContainerReq =
            { Name = "test"
              Image = "nginx"
              Command = "echo 'Hello, World!'" }

        let result = container |> cs.CreateContainer |> Async.RunSynchronously

        match result with
        | Ok c -> ctx.WriteJsonAsync c
        | Error e ->
            ctx.SetStatusCode 500
            ctx.WriteJsonAsync(e |> unwrapServiceError))

let containerGetHandler =
    handleContext (fun ctx ->
        let result = Database.getDesiredState

        match result with
        | Ok state -> ctx.WriteJsonAsync state
        | Error e -> ctx.WriteJsonAsync e.Message)

let webApp =
    choose
        [ POST >=> choose [ route "/container" >=> containerCreateHandler ]
          GET >=> choose [ route "/container" >=> containerGetHandler ] ]

let configureApp (app: IApplicationBuilder) = app.UseGiraffe webApp

let configureServices (services: IServiceCollection) = services.AddGiraffe() |> ignore

[<EntryPoint>]
let main _ =
    Host
        .CreateDefaultBuilder()
        .ConfigureWebHostDefaults(fun webHostBuilder ->
            webHostBuilder.Configure(configureApp).ConfigureServices(configureServices)
            |> ignore)
        .Build()
        .Run()

    ctks.Cancel()
    0
