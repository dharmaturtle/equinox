module Domain.Nonrecord

open FsCodec.NewtonsoftJson
open FSharp.UMX
open Newtonsoft.Json
open System

type MyStreamId = Guid<myStreamId>
and [<Measure>] myStreamId
type MyUmxGuid = Guid<myUmxGuid>
and [<Measure>] myUmxGuid

let streamName (id: MyStreamId) = FsCodec.StreamName.create "MyStream" (string id)

// NOTE - these types and the union case names reflect the actual storage formats and hence need to be versioned with care
[<RequireQualifiedAccess>]
module Events =

    type Snapshot =
        { MyUmxGuid: MyUmxGuid
          Guid   : Guid }

    type Event =
        | Snapshotted      of Snapshot
        | MyUmxGuidChanged of MyUmxGuid
        | GuidChanged      of Guid
        interface TypeShape.UnionContract.IUnionContract
    let codec = FsCodec.NewtonsoftJson.Codec.Create<Event>()

module Fold =
    type State =
        | Initial
        | Active of Events.Snapshot
    let mapActive f = function
        | Active a -> f a |> Active
        | x -> x
    let initial = State.Initial
    let evolve (state : State) event =
        match event with
        | Events.Snapshotted s -> s |> Active
        | Events.MyUmxGuidChanged g ->
            state |> mapActive (fun a -> { a with Events.MyUmxGuid = g })
        | Events.GuidChanged g ->
            state |> mapActive (fun a -> { a with Events.Guid = g })
        
    let fold : State -> Events.Event seq -> State = Seq.fold evolve
    let isOrigin = function Events.Snapshotted _ -> true | _ -> false

type Command =
    | ChangeMyUmxGuid of MyUmxGuid
    | ChangeGuid      of Guid
    | Activate        of Events.Snapshot

let interpret (cmd : Command) (state : Fold.State) : Events.Event list =
    let ifActive state e =
        match state with
        | Fold.State.Initial -> []
        | Fold.State.Active _ -> [ e ]
    match cmd with
    | Activate s        -> [ Events.Snapshotted s ]
    | ChangeMyUmxGuid g -> ifActive state (Events.MyUmxGuidChanged g)
    | ChangeGuid g      -> ifActive state (Events.GuidChanged g)

type Service internal (resolve : MyStreamId -> Equinox.Stream<Events.Event, Fold.State>) =

    member __.Execute(streamId, cmd : Command) : Async<unit> =
        let stream = resolve streamId
        stream.Transact(interpret cmd)

    member __.Read streamId =
        let stream = resolve (streamId)
        stream.Query id

let create log resolve =
    let resolve id =
        let stream = resolve (streamName id)
        Equinox.Stream(log, stream, maxAttempts = 3)
    Service resolve
