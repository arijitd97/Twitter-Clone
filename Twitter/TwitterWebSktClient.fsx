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


// number of user
let configuration = 
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

let system = ActorSystem.Create("TwitterSim", configuration)
let echoServer = new WebSocket("ws://localhost:8080/websocket")
echoServer.OnOpen.Add(fun args -> System.Console.WriteLine("Open"))
echoServer.OnClose.Add(fun args -> System.Console.WriteLine("Close"))
echoServer.OnMessage.Add(fun args -> System.Console.WriteLine("Msg: {0}", args.Data))
echoServer.OnError.Add(fun args -> System.Console.WriteLine("Error: {0}", args.Message))

echoServer.Connect()

type MessageType = {
    OperationName : string
    UserName : string
    Password : string
    SubscribeUserName : string
    TweetData : string
    Queryhashtag : string
    QueryAt : string
}


let TwitterClient (mailbox: Actor<string>)=
    let mutable userName = ""
    let mutable password = ""

    let rec loop () = actor {        
        let! message = mailbox.Receive ()
        let sender = mailbox.Sender()
        
        let result = message.Split ','
        let operation = result.[0]

        if operation = "Register" then
            userName <- result.[1]
            password <- result.[2]
            let serverJson: MessageType = {OperationName = "register"; UserName = userName; Password = password; SubscribeUserName = ""; TweetData = ""; Queryhashtag = ""; QueryAt = ""} 
            let json = Json.serialize serverJson
            echoServer.Send json
            return! loop()  
        else if operation = "Subscribe" then
            let serverJson: MessageType = {OperationName = "subscribe"; UserName = userName; Password = password; SubscribeUserName = result.[1]; TweetData = ""; Queryhashtag = ""; QueryAt = ""} 
            let json = Json.serialize serverJson
            echoServer.Send json
        else if operation = "Tweet" then
            let serverJson: MessageType = {OperationName = "tweet"; UserName = userName; Password = password; SubscribeUserName = ""; TweetData = "Tweeted By "+userName+": "+result.[1]; Queryhashtag = ""; QueryAt = ""} 
            let json = Json.serialize serverJson
            echoServer.Send json
            sender <? "success" |> ignore
        else if operation = "RecievedTweet" then
            printfn "[%s] : %s" userName result.[1]  
        else if operation = "Querying" then
            let serverJson: MessageType = {OperationName = "querying"; UserName = userName; Password = password; SubscribeUserName = ""; TweetData = ""; Queryhashtag = ""; QueryAt = ""} 
            let json = Json.serialize serverJson
            echoServer.Send json
            sender <? "success" |> ignore 
        else if operation = "Logout" then
            let serverJson: MessageType = {OperationName = "logout"; UserName = userName; Password = password; SubscribeUserName = ""; TweetData = ""; Queryhashtag = ""; QueryAt = ""} 
            let json = Json.serialize serverJson
            echoServer.Send json
        else if operation = "QueryHashtags" then
            let serverJson: MessageType = {OperationName = "hashtag"; UserName = userName; Password = password; SubscribeUserName = ""; TweetData = ""; Queryhashtag = result.[1]; QueryAt = ""} 
            let json = Json.serialize serverJson
            echoServer.Send json
            sender <? "success" |> ignore 
        else if operation = "QueryMentions" then            
            let serverJson: MessageType = {OperationName = "mention"; UserName = userName; Password = password; SubscribeUserName = ""; TweetData = ""; Queryhashtag =""; QueryAt =  result.[1]} 
            let json = Json.serialize serverJson
            echoServer.Send json
            sender <? "success" |> ignore 
        else if operation = "Retweet" then            
            let serverJson: MessageType = {OperationName = "retweet"; UserName = userName; Password = password; SubscribeUserName = ""; TweetData = result.[1]; Queryhashtag =""; QueryAt =  ""} 
            let json = Json.serialize serverJson
            echoServer.Send json
            sender <? "success" |> ignore
        else if operation = "Login" then
            userName <- result.[1]
            password <- result.[2]
            let serverJson: MessageType = {OperationName = "login"; UserName = userName; Password = password; SubscribeUserName = ""; TweetData = ""; Queryhashtag = ""; QueryAt = ""} 
            let json = Json.serialize serverJson
            echoServer.Send json 
        return! loop()     
    }
    loop ()


let client = spawn system ("User"+(string 1)) TwitterClient
let rec readInput () =
    Console.Write("Enter command: ")
    let input = Console.ReadLine()
    let inpMessage = input.Split ','
    let serverOp = inpMessage.[0]
    
    match (serverOp) with
    | "Register" -> 
        let username = inpMessage.[1]
        let password = inpMessage.[2]    
        client <! "Register,"+username+","+password
        readInput()
    | "Login" -> 
        let username = inpMessage.[1]
        let password = inpMessage.[2]    
        client <! "Login,"+username+","+password
        readInput()
    | "Subscribe" ->
        let username = inpMessage.[1] 
        client <! "Subscribe,"+username
        readInput()
    | "Tweet" ->
        let message = inpMessage.[1] 
        client <! "Tweet,"+message
        readInput()
    | "Query" ->
        client <! "Querying"
        readInput()
    | "QueryHashtag" ->
        client <! "QueryHashtags,"+inpMessage.[1]
        readInput()
    | "QueryMention" ->
        client <! "QueryMentions,"+inpMessage.[1]
        readInput()
    | "Retweet" ->
        client <! "Retweet,"+inpMessage.[1]
        readInput()
    | "Logout" ->
        client <! "Logout"
        readInput()
    | "Exit" ->
        printfn "Exiting Client!"
    | _ -> 
        printfn "Invalid Input, Please refer the Report"
        readInput()


readInput()

system.Terminate() |> ignore
0 