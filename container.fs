module ContainerStreaming

open Docker.DotNet
open System
open Docker.DotNet.Models
open Microsoft.Extensions.Hosting
open System.Collections.Generic
open System.Collections.Concurrent

type ContainerState =
    | Created
    | Running
    | Exited


module ContainerName =
    type ContainerName = ContainerName of string

    let Create (name: string) =
        let n = name.Replace("/", "")
        ContainerName n

    let Unwrap (ContainerName n) = n

type Container =
    { ID: string
      Image: string
      Name: ContainerName.ContainerName
      Status: ContainerState }


type ContainerAction =
    | Start
    | Create
    | Destroy
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
    | "create" -> Ok Create
    | "destroy" -> Ok Destroy
    | "die" -> Ok Die
    | _ -> Error("Unknown action: " + str)

let toContainerState str =
    match str with
    | "running" -> Ok Running
    | "created" -> Ok Created
    | "exited" -> Ok Exited
    | _ -> Error("Unknown state: " + str)


let mutable state = ConcurrentDictionary<ContainerName.ContainerName, Container>()

let dockerEventProgressHandler (client: DockerClient) =

    let handleStartAction pm =
        let containerInspection =
            client.Containers.InspectContainerAsync(pm.ID)
            |> Async.AwaitTask
            |> Async.RunSynchronously

        let containerStateResult = toContainerState containerInspection.State.Status

        match containerStateResult with
        | Ok res ->
            let container =
                { ID = pm.ID
                  Name = ContainerName.Create containerInspection.Name
                  Image = containerInspection.Image
                  Status = res }

            printfn "%A" container
            state.TryAdd(container.Name, container) |> ignore
        | Error e -> printfn "%s" e

    let handleDestroyAction pm =
        printfn "Removing container %A" pm

        let c = state.Values |> Seq.tryFind (fun x -> x.ID = pm.ID)

        match c with
        | Some c ->
            match state.TryRemove(c.Name) with
            | (true, v) -> printfn "Removed container %A" c.Name
            | (false, v) -> printfn "Failed to remove container %A" c.Name
        | None -> printfn "Container not found: %s" pm.ID

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
            | Start -> handleStartAction pm
            | Create -> handleStartAction pm
            | Destroy -> handleDestroyAction pm
            | Die -> printfn "Container %s has died" pm.ID
            | Disconnect -> printfn "Container %s has disconnected" pm.ID
            | Kill -> printfn "Container %s has been killed" pm.ID
            | Stop -> printfn "Container %s has been stopped" pm.ID
        | Error e -> printfn "%s" e

    messageHandler

let initState (client: DockerClient) ctk =
    task {
        let containerListParams = new ContainersListParameters()
        let filter = Dictionary<string, IDictionary<string, bool>>()
        let labelKey = "orchestration_owner"
        let labelDict = Dictionary<string, bool>()
        labelDict.Add(sprintf "%s=%s" labelKey "glide", true)
        filter.Add("label", labelDict)
        containerListParams.All <- true
        containerListParams.Filters <- filter

        let! containers = client.Containers.ListContainersAsync(containerListParams, ctk)

        for container in containers do
            let status = toContainerState container.State

            match status with
            | Ok s ->
                let c =
                    { Name = ContainerName.Create container.Names.[0]
                      Image = container.Image
                      Status = s
                      ID = container.ID }

                state.TryAdd(c.Name, c) |> ignore
            | Error e -> printfn "%s" e

        printfn "Found %d matching containers" state.Count
    }


let listen_to_changes (client: DockerClient) ctk =
    let progress =
        Progress<Message>(dockerEventProgressHandler (client)) :> IProgress<Message>

    client.System.MonitorEventsAsync(ContainerEventsParameters(), progress, ctk)
    |> Async.AwaitTask
    |> Async.Start

type ReconcileService(reconcileTime, client: DockerClient) =
    inherit BackgroundService()

    override r.ExecuteAsync(ctk) =
        task {
            let interval = TimeSpan.FromMilliseconds reconcileTime
            let periodic = new System.Threading.PeriodicTimer(interval)

            while not ctk.IsCancellationRequested do
                let! _ = periodic.WaitForNextTickAsync(ctk)
                let desiredState = Database.getDesiredState ()

                match desiredState with
                | Ok s ->
                    for ds in s do
                        let name = ContainerName.Create ds.Name

                        match state.ContainsKey(name) with
                        | true ->
                            // since we found the container, we just have to diff and check if it's still in the proper state
                            ()
                        | false ->
                            printfn "Did not find the desired container in the existing state: %A" name
                            // create the container with the desired state
                            let createContainer: CreateContainerParameters = new CreateContainerParameters()
                            let labels = new Dictionary<string, string>()
                            labels.Add("glide:name", ContainerName.Unwrap name)
                            labels.Add("orchestration_owner", "glide")
                            createContainer.Name <- ds.Name
                            createContainer.Image <- ds.Image

                            createContainer.Labels <- labels

                            try
                                let! res = client.Containers.CreateContainerAsync(createContainer, ctk)
                                printfn "%A" res
                            with ex when ex.Message.Contains("Conflict") ->
                                printfn "Container already exists"

                | Error e -> printfn "Error occurred while fetching desired state: %s" e.Message
        }
