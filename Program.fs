open Docker.DotNet
open System
open Docker.DotNet.Models
open System.Threading
open Fumble
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection
open Giraffe

open ContainerStreaming

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

    let saveDesiredState (state: CreateContainerReq) =

        let insert =
            sqliteConnectionString
            |> Sql.connect
            |> Sql.query "INSERT INTO DesiredContainerState (name, image, command) VALUES (@name, @image, @command)"
            |> Sql.parameters
                [ "@name", Sql.string state.Name
                  "@image", Sql.string state.Image
                  "@command", Sql.string state.Command ]
            |> Sql.executeNonQuery

        match insert with
        | Ok _ -> ()
        | Error e -> failwith e.Message


type ContainerService() =
    member _.lock: SemaphoreSlim = new SemaphoreSlim(1)

    member cs.CreateContainer(container: Database.CreateContainerReq) =
        async {
            let! acquired = (cs.lock.WaitAsync(100)) |> Async.AwaitTask

            if acquired then
                try
                    printfn "Creating container %s" container.Name

                    Database.saveDesiredState
                        { Name = container.Name
                          Image = container.Image
                          Command = container.Command }
                finally
                    printfn "Releasing lock"
                    let count = cs.lock.Release()

                    printfn "Released lock: %d" count
            else
                printfn "Failed to acquire lock"
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

        container |> cs.CreateContainer |> Async.RunSynchronously

        ctx.WriteJsonAsync container)

let containerGetHandler =
    handleContext (fun ctx ->
        let result = Database.getDesiredState

        match result with
        | Ok state -> ctx.WriteJsonAsync state
        | Error e -> ctx.WriteJsonAsync e.Message)

let webApp =
    choose
        [ route "/ping" >=> text "pong"
          POST >=> choose [ route "/container" >=> containerCreateHandler ]
          GET >=> choose [ route "/container" >=> containerGetHandler ]
          route "/" >=> htmlFile "/pages/index.html" ]

let configureApp (app: IApplicationBuilder) = app.UseGiraffe webApp

let configureServices (services: IServiceCollection) =
    // Add Giraffe dependencies
    services.AddGiraffe() |> ignore

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
