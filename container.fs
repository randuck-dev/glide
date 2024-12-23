module ContainerStreaming

open Docker.DotNet
open System
open Docker.DotNet.Models

type Container =
    { ID: string
      Name: string
      Status: string }

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
