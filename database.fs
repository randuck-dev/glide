module Database

open Fumble

let sqliteConnectionString = "Data Source=containers.db"


[<CLIMutable>]
type CreateContainerReq =
    { Image: string
      Name: string
      Command: string }

type DesiredContainerState =
    { Id: string option
      Name: string
      Image: string
      Command: string }

let getDesiredState () =
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