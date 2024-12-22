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


let sqliteConnectionString = "Data Source=containers.db"

module Database =
    type DesiredContainerState =
        { Id: string
          Name: string
          Image: string
          Command: string }

    let getDesiredState =
        sqliteConnectionString
        |> Sql.connect
        |> Sql.query "SELECT * FROM DesiredContainerState"
        |> Sql.execute (fun read ->
            { Id = read.string "id"
              Name = read.string "name"
              Image = read.string "image"
              Command = read.string "command" })



let client =
    (new DockerClientConfiguration(Uri("unix:///var/run/docker.sock")))
        .CreateClient()

let token = new CancellationTokenSource()


type Container =
    { ID: string
      Name: string
      Status: string }

module ContainerStreaming =
    type ProgressMessage =
        { ID: string
          Action: string
          Status: string
          Actor: string
          Scope: string }

    let mutable state = Map.empty<string, Container>

    let progressHandler (client: DockerClient) =
        let messageHandler (message: Message) =
            let pm =
                { ID = message.ID
                  Action = message.Action
                  Actor = message.Actor.ID
                  Scope = message.Scope
                  Status = message.Status }

            match pm.Action with
            | "start" ->
                let containerInspection =
                    client.Containers.InspectContainerAsync(pm.ID)
                    |> Async.AwaitTask
                    |> Async.RunSynchronously

                let container =
                    { ID = pm.ID
                      Name = containerInspection.Name
                      Status = containerInspection.State.Status }

                state <- state.Add(pm.ID, container)

            | _ -> printfn "Unable to handle event type %s" pm.Action

        messageHandler


    let listen_to_changes (client: DockerClient) ctk =
        let progress = Progress<Message>(progressHandler (client)) :> IProgress<Message>

        client.System.MonitorEventsAsync(ContainerEventsParameters(), progress, ctk)
        |> Async.AwaitTask
        |> Async.Start

let ctks = new CancellationTokenSource()

ContainerStreaming.listen_to_changes client ctks.Token


let webApp =
    choose
        [ route "/ping" >=> text "pong"
          POST >=> choose [ route "/container" >=> json "hello" ]
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
