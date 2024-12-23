module ContainerStreaming

open Docker.DotNet
open System
open Docker.DotNet.Models


type ContainerState = | Running

type Container =
    { ID: string
      Image: string
      Name: string
      Status: ContainerState }


type ContainerAction =
    | Start
    | Kill
    | Disconnect
    | Stop
    | Die

type ProgressMessage =
    { ID: string
      Action: ContainerAction
      Status: string
      Actor: string
      Scope: string }

let toContainerAction str =
    match str with
    | "start" -> Ok Start
    | "die" -> Ok Die
    | _ -> Error("Unknown action: " + str)

let toContainerState str =
    match str with
    | "running" -> Ok Running
    | _ -> Error("Unknown state: " + str)


let mutable state = Map.empty<string, Container>

let progressHandler (client: DockerClient) =
    let messageHandler (message: Message) =
        let convertedAction = toContainerAction message.Action

        match convertedAction with
        | Ok x ->
            let pm =
                { ID = message.ID
                  Action = x
                  Actor = message.Actor.ID
                  Scope = message.Scope
                  Status = message.Status }

            match pm.Action with
            | Start ->
                let containerInspection =
                    client.Containers.InspectContainerAsync(pm.ID)
                    |> Async.AwaitTask
                    |> Async.RunSynchronously

                let containerStateResult = toContainerState containerInspection.State.Status

                match containerStateResult with
                | Ok res ->
                    let container =
                        { ID = pm.ID
                          Name = containerInspection.Name
                          Image = containerInspection.Image
                          Status = res }

                    printfn "%A" container
                    state <- state.Add(pm.ID, container)
                | Error e -> printfn "%s" e
            | Die -> printfn "Container %s has died" pm.ID
            | Disconnect -> printfn "Container %s has disconnected" pm.ID
            | Kill -> printfn "Container %s has been killed" pm.ID
            | Stop -> printfn "Container %s has been stopped" pm.ID
        | Error e -> printfn "%s" e

    messageHandler


let listen_to_changes (client: DockerClient) ctk =
    let progress = Progress<Message>(progressHandler (client)) :> IProgress<Message>

    client.System.MonitorEventsAsync(ContainerEventsParameters(), progress, ctk)
    |> Async.AwaitTask
    |> Async.Start
