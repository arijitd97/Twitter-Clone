#load "references.fsx"
#time "on"
// #if INTERACTIVE
// #r "nuget: Akka.FSharp"
// #r "nuget: Akka.Remote"
// #r "nuget: Suave"
// #r "nuget: FSharp.Json"
// #r "nuget: websocket-sharp"

// #endif


open System
open Akka.Actor
open Akka.FSharp
open FSharp.Json
open WebSocketSharp
open Akka.Configuration

let config = 
    ConfigurationFactory.ParseString(
        @"akka {
            log-config-on-start : on
            stdout-loglevel : OFF
            loglevel : OFF
            actor {
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                debug : {
                    receive : on
                    autoreceive : on
                    lifecycle : on
                    event-stream : on
                    unhandled : on
                }
            }
            remote {
                helios.tcp {
                    port = 8555
                    hostname = localhost
                }
            }
        }")

let system = ActorSystem.Create("TwitterClient", config)
let server = new WebSocket("ws://localhost:8080/websocket")
server.OnOpen.Add(fun args -> System.Console.WriteLine("SocketOpen"))
server.OnClose.Add(fun args -> System.Console.WriteLine("SocketClose"))
server.OnMessage.Add(fun args -> System.Console.WriteLine("Message: {0}", args.Data))
server.OnError.Add(fun args -> System.Console.WriteLine("Error: {0}", args.Message))

server.Connect()

type MessageType = {
    OperationName : string
    UserName : string
    Password : string
    SubscribeUserName : string
    TweetData : string
    Queryhashtag : string
    Querymention : string
}


let Client (mailbox: Actor<string>)=
    let mutable userName = ""
    let mutable password = ""

    let rec loop () = actor {        
        let! message = mailbox.Receive ()
        let sender = mailbox.Sender()
        let result = message.Split ','
        let op = result.[0]
        match op with
        |   "Register" ->
            userName <- result.[1]
            password <- result.[2]
            let serverJson: MessageType = {OperationName = "register"; UserName = userName; Password = password; SubscribeUserName = ""; TweetData = ""; Queryhashtag = ""; Querymention = ""} 
            let json = Json.serialize serverJson
            server.Send json
            return! loop()  
        |   "Subscribe" ->
            let serverJson: MessageType = {OperationName = "subscribe"; UserName = userName; Password = password; SubscribeUserName = result.[1]; TweetData = ""; Queryhashtag = ""; Querymention = ""} 
            let json = Json.serialize serverJson
            server.Send json
        |   "Tweet" ->
            let serverJson: MessageType = {OperationName = "tweet"; UserName = userName; Password = password; SubscribeUserName = ""; TweetData = "Tweeted By "+userName+": "+result.[1]; Queryhashtag = ""; Querymention = ""} 
            let json = Json.serialize serverJson
            server.Send json
            sender <? "success" |> ignore
        |   "RecievedTweet" ->
            printfn "[%s] : %s" userName result.[1]  
        |   "QueryFeed" ->
            let serverJson: MessageType = {OperationName = "queryfeed"; UserName = userName; Password = password; SubscribeUserName = ""; TweetData = ""; Queryhashtag = ""; Querymention = ""} 
            let json = Json.serialize serverJson
            server.Send json
            sender <? "success" |> ignore 
        |   "Logout" ->
            let serverJson: MessageType = {OperationName = "logout"; UserName = userName; Password = password; SubscribeUserName = ""; TweetData = ""; Queryhashtag = ""; Querymention = ""} 
            let json = Json.serialize serverJson
            server.Send json
        |   "QueryHashtags" ->
            let serverJson: MessageType = {OperationName = "hashtag"; UserName = userName; Password = password; SubscribeUserName = ""; TweetData = ""; Queryhashtag = result.[1]; Querymention = ""} 
            let json = Json.serialize serverJson
            server.Send json
            sender <? "success" |> ignore 
        |   "QueryMentions" ->           
            let serverJson: MessageType = {OperationName = "mention"; UserName = userName; Password = password; SubscribeUserName = ""; TweetData = ""; Queryhashtag =""; Querymention =  result.[1]} 
            let json = Json.serialize serverJson
            server.Send json
            sender <? "success" |> ignore 
        |   "Retweet" ->            
            let serverJson: MessageType = {OperationName = "retweet"; UserName = userName; Password = password; SubscribeUserName = ""; TweetData = result.[1]; Queryhashtag =""; Querymention =  ""} 
            let json = Json.serialize serverJson
            server.Send json
            sender <? "success" |> ignore
        |   "Login" ->
            userName <- result.[1]
            password <- result.[2]
            let serverJson: MessageType = {OperationName = "login"; UserName = userName; Password = password; SubscribeUserName = ""; TweetData = ""; Queryhashtag = ""; Querymention = ""} 
            let json = Json.serialize serverJson
            server.Send json 
        return! loop()     
    }
    loop ()


let client = spawn system ("User") Client
let rec getOperation () =
    // Console.Write("Enter command: ")
    printfn "\n"
    let input = Console.ReadLine()
    let msg = input.Split ','
    let serverOp = msg.[0]
    
    match serverOp with
    | "Register" -> 
        let username = msg.[1]
        let password = msg.[2]    
        client <! "Register,"+username+","+password
        getOperation()
    | "Login" -> 
        let username = msg.[1]
        let password = msg.[2]    
        client <! "Login,"+username+","+password
        getOperation()
    | "Subscribe" ->
        let username = msg.[1] 
        client <! "Subscribe,"+username
        getOperation()
    | "Tweet" ->
        let message = msg.[1] 
        client <! "Tweet,"+message
        getOperation()
    | "Query" ->
        client <! "QueryFeed"
        getOperation()
    | "QueryHashtag" ->
        client <! "QueryHashtags,"+msg.[1]
        getOperation()
    | "QueryMention" ->
        client <! "QueryMentions,"+msg.[1]
        getOperation()
    | "Retweet" ->
        client <! "Retweet,"+msg.[1]
        getOperation()
    | "Logout" ->
        client <! "Logout"
        getOperation()
    | "Exit" ->
        printfn "Exiting Client!"
    | _ -> 
        printfn "Invalid Input"
        getOperation()

getOperation()

system.Terminate() |> ignore
0 