module Database

open Fumble
open System

let sqliteConnectionString = "Data Source=containers.db"

type PortExposureEntity = { HostPort: int; ContainerPort: int }

type ContainerEntity =
  { Image: string
    Name: string
    Command: string
    Ports: PortExposureEntity list }

type DesiredContainerState =
  { Id: string
    ContainerId: string option
    Name: string
    Image: string
    Command: string
    Ports: PortExposureEntity list }

let toDto ports =
  System.Text.Json.JsonSerializer.Serialize(ports)

let fromDto (ports: string) =
  let res =
    System.Text.Json.JsonSerializer.Deserialize<PortExposureEntity list>(ports)

  match res with
  | null -> []
  | x -> x


let getDesiredState () =
  sqliteConnectionString
  |> Sql.connect
  |> Sql.query "SELECT * FROM DesiredContainerState"
  |> Sql.execute (fun read ->
    let ports = fromDto (read.string "ports")

    { ContainerId = read.stringOrNone "container_id"
      Id = read.string "id"
      Name = read.string "name"
      Image = read.string "image"
      Command = read.string "command"
      Ports = ports })

let getDesiredStateByName name =
  sqliteConnectionString
  |> Sql.connect
  |> Sql.query "SELECT * FROM DesiredContainerState WHERE name = @name"
  |> Sql.parameters [ "@name", Sql.string name ]
  |> Sql.execute (fun read ->
    let ports = fromDto (read.string "ports")

    { ContainerId = read.stringOrNone "container_id"
      Id = read.string "id"
      Name = read.string "name"
      Image = read.string "image"
      Command = read.string "command"
      Ports = ports })



let saveDesiredState (state: ContainerEntity) =
  let id = Guid.NewGuid().ToString()

  let portExposure = toDto state.Ports

  sqliteConnectionString
  |> Sql.connect
  |> Sql.query
    "INSERT INTO DesiredContainerState (id, name, image, command, ports) VALUES (@id, @name, @image, @command, @ports)"
  |> Sql.parameters
    [ "@id", Sql.string id
      "@name", Sql.string state.Name
      "@image", Sql.string state.Image
      "@command", Sql.string state.Command
      "@ports", Sql.string portExposure ]
  |> Sql.executeNonQuery
