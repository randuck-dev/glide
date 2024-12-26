module Database

open Fumble

let sqliteConnectionString = "Data Source=containers.db"


type ContainerEntity =
  { Image: string
    Name: string
    Command: string
    HostPort: int
    ContainerPort: int }

type DesiredContainerState =
  { Id: string option
    Name: string
    Image: string
    Command: string
    HostPort: int
    ContainerPort: int }

let getDesiredState () =
  sqliteConnectionString
  |> Sql.connect
  |> Sql.query "SELECT * FROM DesiredContainerState"
  |> Sql.execute (fun read ->
    { Id = read.stringOrNone "id"
      Name = read.string "name"
      Image = read.string "image"
      Command = read.string "command"
      HostPort = read.int "host_port"
      ContainerPort = read.int "container_port" })

let getDesiredStateByName name =
  sqliteConnectionString
  |> Sql.connect
  |> Sql.query "SELECT * FROM DesiredContainerState WHERE name = @name"
  |> Sql.parameters [ "@name", Sql.string name ]
  |> Sql.execute (fun read ->
    { Id = read.stringOrNone "id"
      Name = read.string "name"
      Image = read.string "image"
      Command = read.string "command"
      HostPort = read.int "host_port"
      ContainerPort = read.int "container_port" })

let saveDesiredState (state: ContainerEntity) =
  sqliteConnectionString
  |> Sql.connect
  |> Sql.query
    "INSERT INTO DesiredContainerState (name, image, command, container_port, host_port) VALUES (@name, @image, @command, @containerPort, @hostPort)"
  |> Sql.parameters
    [ "@name", Sql.string state.Name
      "@image", Sql.string state.Image
      "@command", Sql.string state.Command
      "@containerPort", Sql.int state.ContainerPort
      "@hostPort", Sql.int state.HostPort ]
  |> Sql.executeNonQuery
