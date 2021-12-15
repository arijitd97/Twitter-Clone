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
// open Suave.Http
open Suave.Operators
open Suave.Filters
open Suave.Successful
// open Suave.Files
open Suave.RequestErrors
open Suave.Logging
// open Suave.Utils
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
    // | ReTweet of  string  * string * string
    | Querying of  string  * string 
    | QueryHashTag of   string   
    | QueryAt of   string  
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
      let mutable res = ""
      if isRetweet then
        res <- sprintf "[Retweet][TweetID=%s]%s" this.TweetId this.TweetString
      else
        res <- sprintf "[TweetId=%s]%s" this.TweetId this.TweetString
      res

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
        |   Register(username,password,webSocket) ->
            if username = "" then
                return! loop()
            /////////
            let mutable res:ResponseType = {Status=""; Data=""}
            if userNameMap.ContainsKey(username) then
                res<-{Status="Error"; Data="Register: Username already exists!"}
            else
                let user = User(username, password, webSocket)
                // this.AddUser user
                userNameMap <- userNameMap.Add(user.UserName, user)
                user.AddToFollowing user
                res<-{Status="Success"; Data="Register: Added successfully!"}
                //res <- "[Register][Success]: " + username + "  Added successfully! "
            // res
            mailbox.Sender() <! res |>ignore
            //////////////
            // mailbox.Sender() <? twitter.Register username password webSocket|> ignore
            
            

        |   Login(username,password,webSocket) ->
            if username = "" then
                return! loop()
            //////////
            let mutable res :ResponseType = {Status=""; Data=""}
            if not (userNameMap.ContainsKey(username)) then
                printfn "%s" "[Login][Error]: Username not found"
                res <- {Status="Error"; Data="Login: Username not found"}
            else
                let user = userNameMap.[username]
                if user.Password = password then
                    user.SetSocket(webSocket)
                    res <- {Status="Success"; Data="User successfully logged in!"}
                else
                    res <- {Status="Error"; Data="Login: Username & password do not match"}

            mailbox.Sender() <! res |>ignore
            // res 
            //////////
            
            // mailbox.Sender() <? twitter.Login username password webSocket|> ignore

        |   Logout(username,password) ->
            //////
            let mutable res:ResponseType = {Status=""; Data=""}
            let mutable auth = false
            if not (userNameMap.ContainsKey(username)) then
                printfn "%s" "[Authentication][Error]: Username not found"
            else
                let user = userNameMap.[username]
                if user.Password = password then
                    auth <- true

            if not (auth) then
                res <- {Status="Error"; Data="Logout: Username & password do not match"}
                //res <- "[Logout][Error]: Username & password do not match"
            else
                if not (userNameMap.ContainsKey(username)) then
                    printfn "%s" "[FetchUserObject][Error]: Username not found"
                else
                    let user = userNameMap.[username]
                // let user = this.GetUser username
                    user.Logout()  
            mailbox.Sender() <! res |>ignore
            // res
            //////
            // mailbox.Sender() <? twitter.Logout username password |> ignore

        |   Send(username,password,tweetData,reTweet) -> 
            ////////
            let mutable auth = false
            if not (userNameMap.ContainsKey(username)) then
                printfn "%s" "[Authentication][Error]: Username not found"
            else
                let user = userNameMap.[username]
                if user.Password = password then
                    auth <- true
            let mutable res:ResponseType = {Status=""; Data=""}
            if not (auth) then
                res<-{Status="Error"; Data="Sendtweet: Username & password do not match"}
            else
                if not (userNameMap.ContainsKey(username))then
                    res<-{Status="Error"; Data="Sendtweet: Username not found"}
                else
                    let user = userNameMap.[username]
                    let tweet = Tweet(DateTime.Now.ToFileTimeUtc() |> string, tweetData, reTweet)
                    user.AddTweet tweet
                    tweetIdMap <- tweetIdMap.Add(tweet.TweetId,tweet)
                    // this.AddTweet tweet

                    
                    let mentionStart = tweetData.IndexOf("@")
                    if mentionStart <> -1 then
                        let mutable mentionEnd = tweetData.IndexOf(" ",mentionStart)
                        if mentionEnd = -1 then
                            mentionEnd <- tweetData.Length
                        let mention = tweetData.[mentionStart..mentionEnd-1]
                        let key = mention
                        let mutable map = mentionMap
                        if not (map.ContainsKey(key)) then
                            let l = List.empty: Tweet list
                            map <- map.Add(key, l)
                        let value = map.[key]
                        map <- map.Add(key, List.append value [tweet])
                        mentionMap <- map
                        // this.AddToMention mention tweet
                    
                    let hashStart = tweetData.IndexOf("#")
                    if hashStart <> -1 then
                        let mutable hashEnd = tweetData.IndexOf(" ",hashStart)
                        if hashEnd = -1 then
                            hashEnd <- tweetData.Length
                        let hashtag = tweetData.[hashStart..hashEnd-1]
                        let key = hashtag
                        let mutable map = hashtagMap
                        if not (map.ContainsKey(key)) then
                            let l = List.empty: Tweet list
                            map <- map.Add(key, l)
                        let value = map.[key]
                        map <- map.Add(key, List.append value [tweet])
                        hashtagMap <- map
                        // this.AddToHashTag hashtag tweet
                    res<-{Status="Success"; Data="Sent: "+tweet.ToString()}
                    //res <-  "[Sendtweet][Success]: Sent "+tweet.ToString()
                    printfn "%A" hashtagMap
                    printfn "Mention to tweet%A" mentionMap
            // if reTweet then
            //     spSender <! res |>ignore
            // else mailbox.Sender() <! res |>ignore
            mailbox.Sender() <! res |>ignore
            // res
            /// 

            // mailbox.Sender() <? twitter.SendTweet username password tweetData false |> ignore

        |   Subscribe(username,password,subsribeUsername) -> 
            /////////
            let mutable auth = false
            if not (userNameMap.ContainsKey(username)) then
                printfn "%s" "[Authentication][Error]: Username not found"
            else
                let user = userNameMap.[username]
                if user.Password = password then
                    auth <- true
            let mutable res:ResponseType = {Status=""; Data=""}
            if not (auth) then
                res <- {Status="Error"; Data="Subscribe: Username & password do not match"}
            else
                let mutable user1 : User = Unchecked.defaultof<User>
                let mutable user2 : User = Unchecked.defaultof<User>
                if not (userNameMap.ContainsKey(username)) then
                    printfn "%s" "[FetchUserObject][Error]: Username not found"
                else
                    user1 <- userNameMap.[username]
                if not (userNameMap.ContainsKey(subsribeUsername)) then
                    printfn "%s" "[FetchUserObject][Error]: Username not found"
                else
                    user2 <- userNameMap.[subsribeUsername]
                // let user1 = this.GetUser username1
                // let user2 = this.GetUser username2
                user1.AddToFollowing user2
                user2.AddToFollowers user1
                res <- {Status="Success"; Data= username + " now following " + subsribeUsername}
            mailbox.Sender() <! res |>ignore
            // res
            ////////

            // mailbox.Sender() <? twitter.Subscribe username password subsribeUsername |> ignore

        // |   ReTweet(username,password,tweetData) -> 

            ///////
            // let mutable fromSend:ResponseType = {Status=""; Data=""}
            // fromSend <- mailbox.Self <? Send(username,password,tweetData,true,mailbox.Sender())
            // let temp = "[retweet]" + (fromSend).Data
            // let res:ResponseType = {Status="Success"; Data=temp}
            /////// 
            // let mutable res:ResponseType = {Status=""; Data=""}
            // mailbox.Self <! Send(username,password,tweetData,true)
            // mailbox.Sender() <? twitter.ReTweet  username password tweetData |> ignore

        |   Querying(username,password ) -> 
            ///////
            let mutable auth = false
            if not (userNameMap.ContainsKey(username)) then
                printfn "%s" "[Authentication][Error]: Username not found"
            else
                let user = userNameMap.[username]
                if user.Password = password then
                    auth <- true
            let mutable res:ResponseType = {Status=""; Data=""}
            if not (auth) then
                res <- {Status="Error"; Data="QueryTweets: Username & password do not match"}
                //res <- "[QueryTweets][Error]: Username & password do not match"
            else
                let mutable user : User = Unchecked.defaultof<User>
                if not (userNameMap.ContainsKey(username)) then
                    printfn "%s" "[FetchUserObject][Error]: Username not found"
                else
                    user <- userNameMap.[username]
                // let user = this.GetUser username
                let res1 = user.GetFollowing() |> List.map(fun x-> x.GetTweets()) |> List.concat |> List.map(fun x->x.ToString()) |> String.concat "\n"
                res <- {Status="Success"; Data= "\n" + res1}
                //res <- "[QueryTweets][Success] " + "\n" + res1

            mailbox.Sender() <? res |>ignore
            // res
            ///////
            // mailbox.Sender() <? twitter.QueryTweetsSubscribed  username password |> ignore

        |   QueryHashTag(hashtag) -> 
            /////////
            let mutable res:ResponseType = {Status=""; Data=""}
            if not (hashtagMap.ContainsKey(hashtag)) then
                res <- {Status="Error"; Data="QueryHashTags: No Hashtag with given String found"}
                //res <- "[QueryHashTags][Error]: No Hashtag with given String found"
            else
                let res1 = hashtagMap.[hashtag] |>  List.map(fun x->x.ToString()) |> String.concat "\n"
                res <- {Status="Success"; Data= "\n" + res1}
            // res
            mailbox.Sender() <? res |>ignore
            ////////
            // mailbox.Sender() <? twitter.QueryHashTag  queryhashtag |> ignore

        |   QueryAt(mention) -> 
            //////
            let mutable res:ResponseType = {Status=""; Data=""}
            if not (mentionMap.ContainsKey(mention)) then
                res <- {Status="Error"; Data="QueryMentions: No mentions are found for the given user"}
            else
                let res1 = mentionMap.[mention] |>  List.map(fun x->x.ToString()) |> String.concat "\n"
                res <- {Status="Success"; Data= "\n" + res1}
            // res
            mailbox.Sender() <? res |>ignore
            //////
            // mailbox.Sender() <? twitter.QueryMention  at |> ignore
        
        
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
    QueryAt : string
}


type ServerActorMessage =
    | Operation of MessageType* WebSocket

let ServerActor (mailbox: Actor<ServerActorMessage>) = 
    let rec loop () = actor {        
        let! msg = mailbox.Receive ()
        let sender = mailbox.Sender()
        
        match msg  with
        |   Operation(msg,webSocket) ->
            
            printfn "%A" msg.OperationName
            let mutable serverOperation= msg.OperationName
            let mutable username=msg.UserName
            let mutable password=msg.Password
            let mutable subsribeUsername=msg.SubscribeUserName
            let mutable tweetData=msg.TweetData
            let mutable queryhashtag=msg.Queryhashtag
            let mutable querymention=msg.QueryAt
            let mutable task = globalActor <? Register("","",webSocket)
            if serverOperation= "register" then
                printfn "[Register] username:%s password: %s" username password
                task <- globalActor <? Register(username,password,webSocket)
            if serverOperation= "login" then
                printfn "[Login] username:%s password: %s" username password
                task <- globalActor <? Login(username,password,webSocket)
            else if serverOperation= "tweet" then
                printfn "[Tweet] username:%s password: %s tweetData: %s" username password tweetData
                task <- globalActor <? Send(username,password,tweetData,false)
            else if serverOperation= "subscribe" then
                printfn "[Subscribe] username:%s password: %s following username: %s" username password subsribeUsername
                task <- globalActor <? Subscribe(username,password,subsribeUsername )
            else if serverOperation= "querying" then
                printfn "[Query] username:%s password: %s" username password
                task <- globalActor <? Querying(username,password )
            else if serverOperation= "retweet" then
                printfn "[Retweet] username:%s password: %s tweetData: %s" username password (tweetIdMap.[tweetData].TweetString)
                task <- globalActor <? Send(username,password,(tweetIdMap.[tweetData].TweetString),true)
                // printfn "[retweet] username:%s password: %s tweetData: %s" username password (twitter.GetTweetIdToTweetMap().[tweetData].Text)
                // task <- globalActor <? ReTweet(username,password,(twitter.GetTweetIdToTweetMap().[tweetData].Text))
            else if serverOperation= "mention" then
                printfn "[@Mention] %s" querymention
                task <- globalActor <? QueryAt(querymention)
            else if serverOperation= "hashtag" then
                printfn "[#Hashtag] %s: " queryhashtag
                task <- globalActor <? QueryHashTag(queryhashtag )
            else if serverOperation= "logout" then
                task <- globalActor <? Logout(username,password)
            let response: ResponseType = Async.RunSynchronously (task, 1000)
            sender <? response |> ignore
            printfn "[Operation Result: %s] : %s " response.Status response.Data
            return! loop()     
    }
    loop ()
let serverActor = spawn system "ServerActor" ServerActor

serverActor <? "" |> ignore     //Check this line
printfn "*****************************************************" 
printfn "Starting Twitter Server!! ...  " 
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
        printfn "%s" json.OperationName
        //
        let mutable serverOperation= json.OperationName
        let mutable username=json.UserName
        let mutable password=json.Password
        let mutable tweetData=json.TweetData

        // Check if it's send tweet operation
        if serverOperation = "tweet" then
            // let user = twitter.GetUserMap().[username]
            let user = userNameMap.[username]
            let isRetweet = false
            let tweet = Tweet(DateTime.Now.ToFileTimeUtc() |> string, tweetData, isRetweet)
            for follower in user.GetFollowers() do
                if follower.IsLoggedIn() then
                    printfn "Sending message to %s %A" (follower.ToString()) (follower.GetSocket())
                    let byteResponse =
                            (string("ReceivedTweet,"+tweet.TweetString))
                            |> System.Text.Encoding.ASCII.GetBytes
                            |> ByteSegment
                    
                    do! follower.GetSocket().send Text byteResponse true 

        if serverOperation = "retweet" then
            let user = userNameMap.[username]
            let isRetweet = true
            let tweet = Tweet(DateTime.Now.ToFileTimeUtc() |> string, tweetIdMap.[tweetData].TweetString, isRetweet)
            for follower in user.GetFollowers() do
                if follower.IsLoggedIn() then
                    printfn "Sending message to %s %A" (follower.ToString()) (follower.GetSocket())
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

let handleQuery (username,password) = request (fun r ->
  printfn "handlequery %s %s" username password
  let serverJson: MessageType = {OperationName = "querying"; UserName = username; Password = password; SubscribeUserName = ""; TweetData = ""; Queryhashtag = ""; QueryAt = ""} 
  let task = serverActor <? Operation(serverJson,Unchecked.defaultof<WebSocket>)
  let response: ResponseType = Async.RunSynchronously (task, 1000)
  if response.Status = "Success" then
    OK (sprintf "Tweets: %s" response.Data) 
  else
    NOT_FOUND (sprintf "Error: %s" response.Data))

let handleQueryHashtags hashtag = request (fun r ->
  let serverJson: MessageType = {OperationName = "#"; UserName = ""; Password = ""; SubscribeUserName = ""; TweetData = ""; Queryhashtag = "#"+hashtag; QueryAt = ""} 
  let task = serverActor <? Operation(serverJson,Unchecked.defaultof<WebSocket>)
  let response = Async.RunSynchronously (task, 1000)
  if response.Status = "Success" then
    OK (sprintf "Tweets: %s" response.Data) 
  else
    NOT_FOUND (sprintf "Error: %s" response.Data))

let handleQueryMentions mention = request (fun r ->
  let serverJson: MessageType = {OperationName = "@"; UserName = ""; Password = ""; SubscribeUserName = ""; TweetData = ""; Queryhashtag = ""; QueryAt = "@"+mention} 
  let task = serverActor <? Operation(serverJson,Unchecked.defaultof<WebSocket>)
  let response = Async.RunSynchronously (task, 1000)
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
    let serverJson: MessageType = {OperationName = "send"; UserName = tweet.Username; Password = tweet.Password; SubscribeUserName = ""; TweetData = tweet.Data; Queryhashtag = ""; QueryAt = ""} 
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
         pathScan "/query/%s/%s" handleQuery
         pathScan "/queryhashtags/%s" handleQueryHashtags 
         pathScan "/querymentions/%s" handleQueryMentions  
         ]
    POST >=> choose [
         path "/sendtweet" >=> Test  
         ]
    NOT_FOUND "Found no handlers." ]

[<EntryPoint>]
  startWebServer { defaultConfig with logger = Targets.create Verbose [||] } app






