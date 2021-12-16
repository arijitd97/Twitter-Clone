#if INTERACTIVE
#r "nuget: Akka.FSharp"
#r "nuget: Akka.Remote"
#r "nuget: Suave"
#r "nuget: FSharp.Json"

#endif


open System
open Akka.Actor
open Akka.FSharp

open FSharp.Json
open Suave
open Suave.Operators
open Suave.Filters
open Suave.Successful
open Suave.RequestErrors
open Suave.Logging
open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket
open Newtonsoft.Json
open Suave.Writers



type ServerOps =
    | Register of string* string* WebSocket
    | Login of string* string* WebSocket
    | Send of  string  * string * string * bool
    | Subscribe of   string  * string* string 
    | QueryFeed of  string  * string 
    | QueryHashTag of   string   
    | QueryMention of   string  
    | Logout of  string* string

type ResponseType = {
    Status : string
    Data : string
}

let system = ActorSystem.Create("TwitterCloneServer")

type Tweet(tweetId:string, tweetString:string, isRetweet:bool) =
    member this.TweetString = tweetString
    member this.TweetId = tweetId
    member this.IsReTweet = isRetweet

    override this.ToString() =
      let mutable result = ""
      if isRetweet then
        result <- sprintf "[Retweet][TweetID=%s]%s" this.TweetId this.TweetString
      else
        result <- sprintf "[TweetId=%s]%s" this.TweetId this.TweetString
      result

type User(userName:string, password:string, webSocket:WebSocket) =
    let mutable following = List.empty: User list
    let mutable followers = List.empty: User list
    let mutable tweets = List.empty: Tweet list
    let mutable loggedIn = true
    let mutable socket = webSocket
    member this.UserName = userName
    member this.Password = password
    member this.GetFollowing() =
        following
    member this.GetFollowers() =
        followers
    member this.AddToFollowing user =
        following <- List.append following [user]
    member this.AddToFollowers user =
        followers <- List.append followers [user]
    member this.AddTweet x =
        tweets <- List.append tweets [x]
    member this.GetTweets() =
        tweets
    member this.GetSocket() =
        socket
    member this.SetSocket(webSocket) =
        socket <- webSocket
    member this.IsLoggedIn() = 
        loggedIn
    member this.Logout() =
        loggedIn <- false
    override this.ToString() = 
        this.UserName



let mutable tweetIdMap = new Map<string,Tweet>([])
let mutable userNameMap = new Map<string,User>([])
let mutable hashtagMap = new Map<string, Tweet list>([])
let mutable mentionMap = new Map<string, Tweet list>([])
let GlobalActor (mailbox: Actor<_>) =

    let rec loop () = actor {
        let! msg = mailbox.Receive ()
        match msg with
        |   Register(username,password,socket) ->
            if username = "" then
                return! loop()
            let mutable result:ResponseType = {Status=""; Data=""}
            if userNameMap.ContainsKey username then
                result<-{Status="Error"; Data="Register: Username already exists!"}
            else
                let user = User(username, password, socket)
                userNameMap <- userNameMap.Add(user.UserName, user)
                user.AddToFollowing user
                result<-{Status="Success"; Data="Register: User Registered Successfully!"}
            mailbox.Sender() <! result |>ignore
            
        |   Login(username,password,socket) ->
            if username = "" then
                return! loop()
            let mutable result :ResponseType = {Status=""; Data=""}
            if not (userNameMap.ContainsKey(username)) then
                printfn "[Login][Error]: User %s not found" username
                result <- {Status="Error"; Data="Login: User not found"}
            else
                let user = userNameMap.[username]
                if user.Password = password then
                    user.SetSocket(socket)
                    result <- {Status="Success"; Data="User logged in successfully!"}
                else
                    result <- {Status="Error"; Data="Login: Username & password mismatch."}
            mailbox.Sender() <! result |>ignore

        |   Logout(username,password) ->
            let mutable result:ResponseType = {Status=""; Data=""}
            let mutable auth = false

            if userNameMap.ContainsKey username then
                let user = userNameMap.[username]
                if user.Password = password then
                    auth <- true
            else
                printfn "[Authentication][Error]: User %s not found" username

            if auth then
                userNameMap.[username].Logout() 
            else
                result <- {Status="Error"; Data="Logout: Username & password mismatch."}
            mailbox.Sender() <! result |>ignore

        |   Send(username,password,tweetStr,reTweet) -> 
            let mutable auth = false
            if userNameMap.ContainsKey username then
                let user = userNameMap.[username]
                if user.Password = password then
                    auth <- true
            else
                printfn "[Authentication][Error]: User %s not found" username

            let mutable result:ResponseType = {Status=""; Data=""}
            if auth then
                if userNameMap.ContainsKey username then
                    let user = userNameMap.[username]
                    let tweet = Tweet(DateTime.Now.ToFileTimeUtc() |> string, tweetStr, reTweet)
                    user.AddTweet tweet
                    tweetIdMap <- tweetIdMap.Add(tweet.TweetId,tweet)

                    let words = tweetStr.Split ' '
                    for word in words do
                        if word.[0] = '@' then
                            let parsedMention = word.[0..(word.Length-1)]
                            if not (mentionMap.ContainsKey parsedMention) then
                                mentionMap <- Map.add parsedMention List.Empty mentionMap
                            mentionMap <- mentionMap.Add(parsedMention, List.append mentionMap.[parsedMention] [tweet])

                    let words = tweetStr.Split ' '
                    for word in words do
                        if word.[0] = '#' then
                            let parsedHashtag = word.[0..(word.Length-1)]
                            if not (hashtagMap.ContainsKey parsedHashtag) then
                                hashtagMap <- Map.add parsedHashtag List.empty hashtagMap
                            hashtagMap <- hashtagMap.Add(parsedHashtag,List.append hashtagMap.[parsedHashtag] [tweet])

                    result<-{Status="Success"; Data="Sent: "+tweet.ToString()}
                    // printfn "Hashtag %A:" hashtagMap
                    // printfn "Mention %A:" mentionMap
                else
                    result<-{Status="Error"; Data="Tweet/Retweet: User not found"}
            else
                result<-{Status="Error"; Data="Tweet/Retweet: Username & password mismatch."}
            mailbox.Sender() <! result |>ignore

        |   Subscribe(username,password,followingUser) -> 
            let mutable auth = false
            if userNameMap.ContainsKey username then
                let user = userNameMap.[username]
                if user.Password = password then
                    auth <- true
            else
                printfn "[Authentication][Error]: User %s not found" username
            let mutable result:ResponseType = {Status=""; Data=""}
            
            if auth then
                let mutable user1 : User = Unchecked.defaultof<User>
                let mutable user2 : User = Unchecked.defaultof<User>
                if not (userNameMap.ContainsKey username) then
                    printfn "[Error]: User %s not found" username
                else
                    user1 <- userNameMap.[username]
                if not (userNameMap.ContainsKey followingUser) then
                    printfn "[Error]: User %s not registered" followingUser
                    result <- {Status="Error"; Data= "Subscribe: User "+followingUser+" is not registered."}
                else
                    user2 <- userNameMap.[followingUser]
                    user1.AddToFollowing user2
                    user2.AddToFollowers user1
                    result <- {Status="Success"; Data= username + " now following " + followingUser}
            else
                result <- {Status="Error"; Data="Subscribe: Username & password mismatch."}
            mailbox.Sender() <! result |>ignore
            

        |   QueryFeed(username,password) -> 
            let mutable auth = false
            if userNameMap.ContainsKey username then
                let user = userNameMap.[username]
                if user.Password = password then
                    auth <- true
            else
                printfn "[Authentication][Error]: User %s not found" username
            let mutable result:ResponseType = {Status=""; Data=""}
            
            if auth then
                let mutable user : User = Unchecked.defaultof<User>
                if not (userNameMap.ContainsKey username) then
                    printfn "[Error]: User %s not found" username
                else
                    user <- userNameMap.[username]
                let tempRes = user.GetFollowing() |> List.map(fun x-> x.GetTweets()) |> List.concat |> List.map(fun x->x.ToString()) |> String.concat "\n"
                result <- {Status="Success"; Data= "\n" + tempRes}
            else
                result <- {Status="Error"; Data="QueryTweets: Username & password mismatch."}
            mailbox.Sender() <? result |>ignore

        |   QueryHashTag(hashtag) -> 
            let mutable result:ResponseType = {Status=""; Data=""}
            if hashtagMap.ContainsKey hashtag then
                let tempRes = hashtagMap.[hashtag] |>  List.map(fun x->x.ToString()) |> String.concat "\n"
                result <- {Status="Success"; Data= "\n" + tempRes}
            else
                result <- {Status="Error"; Data="QueryHashTags: No tweets with given hashtag found."}
            mailbox.Sender() <? result |>ignore

        |   QueryMention(mention) -> 
            let mutable result:ResponseType = {Status=""; Data=""}
            if mentionMap.ContainsKey mention then
                let tempRes = mentionMap.[mention] |>  List.map(fun x->x.ToString()) |> String.concat "\n"
                result <- {Status="Success"; Data= "\n" + tempRes}
            else
                result <- {Status="Error"; Data="QueryMentions: User has not been mentioned."}
            mailbox.Sender() <? result |>ignore
        
        | _ ->  failwith "Invalid Operation "
        return! loop()
    }
    loop ()



let globalActor = spawn system "globalActor" GlobalActor

type MessageType = {
    OperationName : string
    UserName : string
    Password : string
    SubscribeUserName : string
    TweetData : string
    Queryhashtag : string
    Querymention: string
}


type ServerActorMessage =
    | Operation of MessageType* WebSocket

let ServerActor (mailbox: Actor<ServerActorMessage>) = 
    let rec loop () = actor {        
        let! msg = mailbox.Receive ()
        let sender = mailbox.Sender()
        
        match msg  with
        |   Operation(msg,webSocket) ->
            printfn "\n"
            let mutable serverOperation= msg.OperationName
            let mutable username=msg.UserName
            let mutable password=msg.Password
            let mutable subsribeUsername=msg.SubscribeUserName
            let mutable tweetData=msg.TweetData
            let mutable queryhashtag=msg.Queryhashtag
            let mutable querymention=msg.Querymention
            let mutable promise:Async<ResponseType> = globalActor <? Register("","",webSocket)
            match serverOperation with
            |   "register" ->
                printfn "[Register] username:%s password: %s" username password
                promise <- globalActor <? Register(username,password,webSocket)
            |   "login" ->
                printfn "[Login] username:%s password: %s" username password
                promise <- globalActor <? Login(username,password,webSocket)
            |   "tweet"  ->
                printfn "[Tweet] username:%s password: %s tweetData: %s" username password tweetData
                promise <- globalActor <? Send(username,password,tweetData,false)
            |   "subscribe" ->
                printfn "[Subscribe] username:%s password: %s following username: %s" username password subsribeUsername
                promise <- globalActor <? Subscribe(username,password,subsribeUsername )
            |   "queryfeed" ->
                printfn "[Live Feed] username:%s password: %s" username password
                promise <- globalActor <? QueryFeed(username,password )
            |   "retweet" ->
                printfn "[Retweet] username:%s password: %s tweetData: %s" username password (tweetIdMap.[tweetData].TweetString)
                promise <- globalActor <? Send(username,password,(tweetIdMap.[tweetData].TweetString),true)
            |   "mention" ->
                printfn "[@Mention] %s" querymention
                promise <- globalActor <? QueryMention(querymention)
            |   "hashtag" ->
                printfn "[#Hashtag] %s: " queryhashtag
                promise <- globalActor <? QueryHashTag(queryhashtag )
            |   "logout" ->
                promise <- globalActor <? Logout(username,password)
            |   _ ->  failwith "Invalid Operation "

            let response: ResponseType = Async.RunSynchronously (promise, 1000)
            sender <? response |> ignore
            printfn "[Operation Result: %s] : %s " response.Status response.Data
            return! loop()     
    }
    loop ()
let serverActor = spawn system "ServerActor" ServerActor


printfn "*****************************************************" 
printfn "Twitter Server Up and running.." 
printfn "*****************************************************"


let ws (webSocket : WebSocket) (context: HttpContext) =
  socket {
    let mutable loop = true

    while loop do
      let! msg = webSocket.read()
      match msg with
      
      | (Text, data, true) ->
        let str = UTF8.toString data
        let mutable json = Json.deserialize<MessageType> str
        // printfn "%s" json.OperationName
        let mutable serverOperation= json.OperationName
        let mutable username=json.UserName
        let mutable password=json.Password
        let mutable tweetData=json.TweetData

        if serverOperation = "tweet" then
            let user = userNameMap.[username]
            let isRetweet = false
            let tweet = Tweet(DateTime.Now.ToFileTimeUtc() |> string, tweetData, isRetweet)
            for follower in user.GetFollowers() do
                if follower.IsLoggedIn() then
                    // printfn "Sending message to %s %A" (follower.ToString()) (follower.GetSocket())
                    let byteResponse =
                            (string("ReceivedTweet:"+tweet.TweetString))
                            |> System.Text.Encoding.ASCII.GetBytes
                            |> ByteSegment
                    
                    do! follower.GetSocket().send Text byteResponse true 

        if serverOperation = "retweet" then
            let user = userNameMap.[username]
            let isRetweet = true
            let tweet = Tweet(DateTime.Now.ToFileTimeUtc() |> string, tweetIdMap.[tweetData].TweetString, isRetweet)
            for follower in user.GetFollowers() do
                if follower.IsLoggedIn() then
                    // printfn "Sending message to %s %A" (follower.ToString()) (follower.GetSocket())
                    let byteResponse =
                            (string("[Retweet] ReceivedTweet:"+tweet.TweetString))
                            |> System.Text.Encoding.ASCII.GetBytes
                            |> ByteSegment
                    
                    do! follower.GetSocket().send Text byteResponse true                 

        let mutable task = serverActor <? Operation(json,webSocket)
        let response: ResponseType = Async.RunSynchronously (task, 10000)

        let byteResponse =
          Json.serialize response
          |> System.Text.Encoding.ASCII.GetBytes
          |> ByteSegment

        do! webSocket.send Text byteResponse true

      | (Close, _, _) ->
        let emptyResponse = [||] |> ByteSegment
        do! webSocket.send Close emptyResponse true

        loop <- false

      | _ -> ()
    }


let getFeed (username,password) = request (fun r ->
  let serverJson: MessageType = {OperationName = "queryfeed"; UserName = username; Password = password; SubscribeUserName = ""; TweetData = ""; Queryhashtag = ""; Querymention = ""} 
  let promise = serverActor <? Operation(serverJson,Unchecked.defaultof<WebSocket>)
  let response: ResponseType = Async.RunSynchronously (promise, 1000)
  if response.Status = "Success" then
    OK (sprintf "Tweets: %s" response.Data) 
  else
    NOT_FOUND (sprintf "Error: %s" response.Data))

let getHashtag hashtag = request (fun r ->
  let serverJson: MessageType = {OperationName = "hashtag"; UserName = ""; Password = ""; SubscribeUserName = ""; TweetData = ""; Queryhashtag = "#"+hashtag; Querymention = ""} 
  let promise = serverActor <? Operation(serverJson,Unchecked.defaultof<WebSocket>)
  let response = Async.RunSynchronously (promise, 1000)
  if response.Status = "Success" then
    OK (sprintf "Tweets: %s" response.Data) 
  else
    NOT_FOUND (sprintf "Error: %s" response.Data))

let getMention mention = request (fun r ->
  let serverJson: MessageType = {OperationName = "mention"; UserName = ""; Password = ""; SubscribeUserName = ""; TweetData = ""; Queryhashtag = ""; Querymention = "@"+mention} 
  let promise = serverActor <? Operation(serverJson,Unchecked.defaultof<WebSocket>)
  let response = Async.RunSynchronously (promise, 1000)
  if response.Status = "Success" then
    OK (sprintf "Tweets: %s" response.Data) 
  else
    NOT_FOUND (sprintf "Error: %s" response.Data))

type SendTweetType = {
    Username: string
    Password: string
    Data : string
}

let getString (rawForm: byte[]) =
    System.Text.Encoding.UTF8.GetString(rawForm)

let fromJson<'a> json =
    JsonConvert.DeserializeObject(json, typeof<'a>) :?> 'a

let HandleTweet (tweet: SendTweetType) = 
    let serverJson: MessageType = {OperationName = "send"; UserName = tweet.Username; Password = tweet.Password; SubscribeUserName = ""; TweetData = tweet.Data; Queryhashtag = ""; Querymention = ""} 
    let task = serverActor <? Operation(serverJson,Unchecked.defaultof<WebSocket>)
    let response = Async.RunSynchronously (task, 1000)
    response

let Test  = request (fun r ->
    let response = r.rawForm
                |> getString
                |> fromJson<SendTweetType>
                |> HandleTweet
    if response.Status = "Error" then
        response
            |> JsonConvert.SerializeObject
            |> UNAUTHORIZED
    else
        response
            |> JsonConvert.SerializeObject
            |> CREATED) >=> setMimeType "application/json"



let app : WebPart = 
  choose [
    path "/websocket" >=> handShake ws
    path "/websocketWithSubprotocol" >=> handShakeWithSubprotocol (chooseSubprotocol "test") ws
    GET >=> choose [
         pathScan "/query/%s/%s" getFeed
         pathScan "/queryhashtags/%s" getHashtag 
         pathScan "/querymentions/%s" getMention  
         ]
    POST >=> choose [
         path "/sendtweet" >=> Test  
         ]
    NOT_FOUND "Found no handlers." ]

[<EntryPoint>]
  startWebServer { defaultConfig with logger = Targets.create Verbose [||] } app






