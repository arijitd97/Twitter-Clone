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
open Suave.Http
open Suave.Operators
open Suave.Filters
open Suave.Successful
open Suave.Files
open Suave.RequestErrors
open Suave.Logging
open Suave.Utils
open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket
open Newtonsoft.Json
open Suave.Writers



type ServerOps =
    | Register of string* string* WebSocket
    | Login of string* string* WebSocket
    | Send of  string  * string * string * bool * IActorRef
    | Subscribe of   string  * string* string 
    | ReTweet of  string  * string * string
    | Querying of  string  * string 
    | QueryHashTag of   string   
    | QueryAt of   string  
    | Logout of  string* string

type ResponseType = {
    Status : string
    Data : string
}


let system = ActorSystem.Create("TwitterServer")



type Tweet(tweetId:string, text:string, isRetweet:bool) =
    member this.Text = text
    member this.TweetId = tweetId
    member this.IsReTweet = isRetweet

    override this.ToString() =
      let mutable res = ""
      if isRetweet then
        res <- sprintf "[retweet][%s]%s" this.TweetId this.Text
      else
        res <- sprintf "[%s]%s" this.TweetId this.Text
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

// type Twitter() =
//     let mutable tweetIdToTweetMap = new Map<string,Tweet>([])
//     let mutable usernameToUserObjMap = new Map<string,User>([])
//     let mutable hashtagToTweetMap = new Map<string, Tweet list>([])
//     let mutable mentionsToTweetMap = new Map<string, Tweet list>([])
//     member this.GetUserMap() = 
//          usernameToUserObjMap
//     member this.GetTweetIdToTweetMap() = 
//         tweetIdToTweetMap
//     member this.AddUser (user:User) =
//         usernameToUserObjMap <- usernameToUserObjMap.Add(user.UserName, user)
//     member this.AddTweet (tweet:Tweet) =
//         tweetIdToTweetMap <- tweetIdToTweetMap.Add(tweet.TweetId,tweet)
//     member this.AddToHashTag hashtag tweet =
//         let key = hashtag
//         let mutable map = hashtagToTweetMap
//         if not (map.ContainsKey(key)) then
//             let l = List.empty: Tweet list
//             map <- map.Add(key, l)
//         let value = map.[key]
//         map <- map.Add(key, List.append value [tweet])
//         hashtagToTweetMap <- map
//     member this.AddToMention mention tweet = 
//         let key = mention
//         let mutable map = mentionsToTweetMap
//         if not (map.ContainsKey(key)) then
//             let l = List.empty: Tweet list
//             map <- map.Add(key, l)
//         let value = map.[key]
//         map <- map.Add(key, List.append value [tweet])
//         mentionsToTweetMap <- map
//     member this.Register username password webSocket=
//         //let mutable res = ""
//         let mutable res:ResponseType = {Status=""; Data=""}
//         if usernameToUserObjMap.ContainsKey(username) then
//             res<-{Status="Error"; Data="Register: Username already exists!"}
//             //res <- "[Register][Error]: Username already exists!"
//         else
//             let user = User(username, password, webSocket)
//             this.AddUser user
//             user.AddToFollowing user
//             res<-{Status="Success"; Data="Register: Added successfully!"}
//             //res <- "[Register][Success]: " + username + "  Added successfully! "
//         res
//     member this.SendTweet username password text isRetweet =
//         let mutable res:ResponseType = {Status=""; Data=""}
//         if not (this.Authentication username password) then
//             res<-{Status="Error"; Data="Sendtweet: Username & password do not match"}
//             //res <- "[Sendtweet][Error]: Username & password do not match"
//         else
//             if not (usernameToUserObjMap.ContainsKey(username))then
//                 res<-{Status="Error"; Data="Sendtweet: Username not found"}
//                 //res <-  "[Sendtweet][Error]: Username not found"
//             else
//                 let user = usernameToUserObjMap.[username]
//                 let tweet = Tweet(DateTime.Now.ToFileTimeUtc() |> string, text, isRetweet)
//                 user.AddTweet tweet
//                 this.AddTweet tweet

                
//                 let mentionStart = text.IndexOf("@")
//                 if mentionStart <> -1 then
//                     let mutable mentionEnd = text.IndexOf(" ",mentionStart)
//                     if mentionEnd = -1 then
//                         mentionEnd <- text.Length
//                     let mention = text.[mentionStart..mentionEnd-1]
//                     this.AddToMention mention tweet
                
//                 let hashStart = text.IndexOf("#")
//                 if hashStart <> -1 then
//                     let mutable hashEnd = text.IndexOf(" ",hashStart)
//                     if hashEnd = -1 then
//                         hashEnd <- text.Length
//                     let hashtag = text.[hashStart..hashEnd-1]
//                     this.AddToHashTag hashtag tweet
//                 res<-{Status="Success"; Data="Sent: "+tweet.ToString()}
//                 //res <-  "[Sendtweet][Success]: Sent "+tweet.ToString()
//                 printfn "%A" hashtagToTweetMap
//                 printfn "Mention to tweet%A" mentionsToTweetMap
//         res
//     member this.Authentication username password =
//             let mutable res = false
//             if not (usernameToUserObjMap.ContainsKey(username)) then
//                 printfn "%s" "[Authentication][Error]: Username not found"
//             else
//                 let user = usernameToUserObjMap.[username]
//                 if user.Password = password then
//                     res <- true
//             res
//     member this.Login username password webSocket=
//             let mutable res :ResponseType = {Status=""; Data=""}
//             if not (usernameToUserObjMap.ContainsKey(username)) then
//                 printfn "%s" "[Login][Error]: Username not found"
//                 res <- {Status="Error"; Data="Login: Username not found"}
//             else
//                 let user = usernameToUserObjMap.[username]
//                 if user.Password = password then
//                    user.SetSocket(webSocket)
//                    res <- {Status="Success"; Data="User successfully logged in!"}
//                 else
//                     res <- {Status="Error"; Data="Login: Username & password do not match"}

//             res
//     member this.GetUser username = 
//         let mutable res : User = Unchecked.defaultof<User>
//         if not (usernameToUserObjMap.ContainsKey(username)) then
//             printfn "%s" "[FetchUserObject][Error]: Username not found"
//         else
//             res <- usernameToUserObjMap.[username]
//         res
//     member this.Subscribe username1 password username2 =
//         let mutable res:ResponseType = {Status=""; Data=""}
//         if not (this.Authentication username1 password) then
//             res <- {Status="Error"; Data="Subscribe: Username & password do not match"}
//             //res <- "[Subscribe][Error]: Username & password do not match"
//         else
//             let user1 = this.GetUser username1
//             let user2 = this.GetUser username2
//             user1.AddToFollowing user2
//             user2.AddToFollowers user1
//             res <- {Status="Success"; Data= username1 + " now following " + username2}
//             //res <- "[Subscribe][Success]: " + username1 + " now following " + username2
//         res
//     member this.ReTweet username password text =
//         let temp = "[retweet]" + (this.SendTweet username password text true).Data
//         let res:ResponseType = {Status="Success"; Data=temp}
//         //let res = "[retweet]" + (this.SendTweet username password text true)
//         res
//     member this.QueryTweetsSubscribed username password =
//         let mutable res:ResponseType = {Status=""; Data=""}
//         if not (this.Authentication username password) then
//             res <- {Status="Error"; Data="QueryTweets: Username & password do not match"}
//             //res <- "[QueryTweets][Error]: Username & password do not match"
//         else
//             let user = this.GetUser username
//             let res1 = user.GetFollowing() |> List.map(fun x-> x.GetTweets()) |> List.concat |> List.map(fun x->x.ToString()) |> String.concat "\n"
//             res <- {Status="Success"; Data= "\n" + res1}
//             //res <- "[QueryTweets][Success] " + "\n" + res1
//         res
//     member this.QueryHashTag hashtag =
//         let mutable res:ResponseType = {Status=""; Data=""}
//         if not (hashtagToTweetMap.ContainsKey(hashtag)) then
//             res <- {Status="Error"; Data="QueryHashTags: No Hashtag with given String found"}
//             //res <- "[QueryHashTags][Error]: No Hashtag with given String found"
//         else
//             let res1 = hashtagToTweetMap.[hashtag] |>  List.map(fun x->x.ToString()) |> String.concat "\n"
//             res <- {Status="Success"; Data= "\n" + res1}
//             //res <- "[QueryHashTags][Success] " + "\n" + res1
//         res
//     member this.QueryMention mention =
//         let mutable res:ResponseType = {Status=""; Data=""}
//         if not (mentionsToTweetMap.ContainsKey(mention)) then
//             res <- {Status="Error"; Data="QueryMentions: No mentions are found for the given user"}
//             //res <- "[QueryMentions][Error]: No mentions are found for the given user"
//         else
//             let res1 = mentionsToTweetMap.[mention] |>  List.map(fun x->x.ToString()) |> String.concat "\n"
//             res <- {Status="Success"; Data= "\n" + res1}
//             //res <-  "[QueryMentions][Success]:" + "\n" + res1
//         res
//     member this.Logout username password =
//         let mutable res:ResponseType = {Status=""; Data=""}

//         if not (this.Authentication username password) then
//             res <- {Status="Error"; Data="Logout: Username & password do not match"}
//             //res <- "[Logout][Error]: Username & password do not match"
//         else
//             let user = this.GetUser username
//             user.Logout()  
//         res
//     override this.ToString() =
//         "Snapshot of Twitter"+ "\n" + tweetIdToTweetMap.ToString() + "\n" + usernameToUserObjMap.ToString() + "\n" + hashtagToTweetMap.ToString() + "\n" + mentionsToTweetMap.ToString()
        
    
// let twitter =  Twitter()


let mutable tweetIdToTweetMap = new Map<string,Tweet>([])
let mutable usernameToUserObjMap = new Map<string,User>([])
let mutable hashtagToTweetMap = new Map<string, Tweet list>([])
let mutable mentionsToTweetMap = new Map<string, Tweet list>([])
let GlobalActor (mailbox: Actor<_>) =

    let rec loop () = actor {
        let! msg = mailbox.Receive ()
        match msg with
        |   Register(username,password,webSocket) ->
            if username = "" then
                return! loop()
            /////////
            let mutable res:ResponseType = {Status=""; Data=""}
            if usernameToUserObjMap.ContainsKey(username) then
                res<-{Status="Error"; Data="Register: Username already exists!"}
            else
                let user = User(username, password, webSocket)
                // this.AddUser user
                usernameToUserObjMap <- usernameToUserObjMap.Add(user.UserName, user)
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
            if not (usernameToUserObjMap.ContainsKey(username)) then
                printfn "%s" "[Login][Error]: Username not found"
                res <- {Status="Error"; Data="Login: Username not found"}
            else
                let user = usernameToUserObjMap.[username]
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
            if not (usernameToUserObjMap.ContainsKey(username)) then
                printfn "%s" "[Authentication][Error]: Username not found"
            else
                let user = usernameToUserObjMap.[username]
                if user.Password = password then
                    auth <- true

            if not (auth) then
                res <- {Status="Error"; Data="Logout: Username & password do not match"}
                //res <- "[Logout][Error]: Username & password do not match"
            else
                if not (usernameToUserObjMap.ContainsKey(username)) then
                    printfn "%s" "[FetchUserObject][Error]: Username not found"
                else
                    let user = usernameToUserObjMap.[username]
                // let user = this.GetUser username
                    user.Logout()  
            mailbox.Sender() <! res |>ignore
            // res
            //////
            // mailbox.Sender() <? twitter.Logout username password |> ignore

        |   Send(username,password,tweetData,reTweet,spSender) -> 
            ////////
            let mutable auth = false
            if not (usernameToUserObjMap.ContainsKey(username)) then
                printfn "%s" "[Authentication][Error]: Username not found"
            else
                let user = usernameToUserObjMap.[username]
                if user.Password = password then
                    auth <- true
            let mutable res:ResponseType = {Status=""; Data=""}
            if not (auth) then
                res<-{Status="Error"; Data="Sendtweet: Username & password do not match"}
            else
                if not (usernameToUserObjMap.ContainsKey(username))then
                    res<-{Status="Error"; Data="Sendtweet: Username not found"}
                else
                    let user = usernameToUserObjMap.[username]
                    let tweet = Tweet(DateTime.Now.ToFileTimeUtc() |> string, tweetData, reTweet)
                    user.AddTweet tweet
                    tweetIdToTweetMap <- tweetIdToTweetMap.Add(tweet.TweetId,tweet)
                    // this.AddTweet tweet

                    
                    let mentionStart = tweetData.IndexOf("@")
                    if mentionStart <> -1 then
                        let mutable mentionEnd = tweetData.IndexOf(" ",mentionStart)
                        if mentionEnd = -1 then
                            mentionEnd <- tweetData.Length
                        let mention = tweetData.[mentionStart..mentionEnd-1]
                        let key = mention
                        let mutable map = mentionsToTweetMap
                        if not (map.ContainsKey(key)) then
                            let l = List.empty: Tweet list
                            map <- map.Add(key, l)
                        let value = map.[key]
                        map <- map.Add(key, List.append value [tweet])
                        mentionsToTweetMap <- map
                        // this.AddToMention mention tweet
                    
                    let hashStart = tweetData.IndexOf("#")
                    if hashStart <> -1 then
                        let mutable hashEnd = tweetData.IndexOf(" ",hashStart)
                        if hashEnd = -1 then
                            hashEnd <- tweetData.Length
                        let hashtag = tweetData.[hashStart..hashEnd-1]
                        let key = hashtag
                        let mutable map = hashtagToTweetMap
                        if not (map.ContainsKey(key)) then
                            let l = List.empty: Tweet list
                            map <- map.Add(key, l)
                        let value = map.[key]
                        map <- map.Add(key, List.append value [tweet])
                        hashtagToTweetMap <- map
                        // this.AddToHashTag hashtag tweet
                    res<-{Status="Success"; Data="Sent: "+tweet.ToString()}
                    //res <-  "[Sendtweet][Success]: Sent "+tweet.ToString()
                    printfn "%A" hashtagToTweetMap
                    printfn "Mention to tweet%A" mentionsToTweetMap
            if reTweet then
                spSender <! res |>ignore
            else mailbox.Sender() <! res |>ignore
            // res
            /// 

            // mailbox.Sender() <? twitter.SendTweet username password tweetData false |> ignore

        |   Subscribe(username,password,subsribeUsername) -> 
            /////////
            let mutable auth = false
            if not (usernameToUserObjMap.ContainsKey(username)) then
                printfn "%s" "[Authentication][Error]: Username not found"
            else
                let user = usernameToUserObjMap.[username]
                if user.Password = password then
                    auth <- true
            let mutable res:ResponseType = {Status=""; Data=""}
            if not (auth) then
                res <- {Status="Error"; Data="Subscribe: Username & password do not match"}
            else
                let mutable user1 : User = Unchecked.defaultof<User>
                let mutable user2 : User = Unchecked.defaultof<User>
                if not (usernameToUserObjMap.ContainsKey(username)) then
                    printfn "%s" "[FetchUserObject][Error]: Username not found"
                else
                    user1 <- usernameToUserObjMap.[username]
                if not (usernameToUserObjMap.ContainsKey(subsribeUsername)) then
                    printfn "%s" "[FetchUserObject][Error]: Username not found"
                else
                    user2 <- usernameToUserObjMap.[subsribeUsername]
                // let user1 = this.GetUser username1
                // let user2 = this.GetUser username2
                user1.AddToFollowing user2
                user2.AddToFollowers user1
                res <- {Status="Success"; Data= username + " now following " + subsribeUsername}
            mailbox.Sender() <! res |>ignore
            // res
            ////////

            // mailbox.Sender() <? twitter.Subscribe username password subsribeUsername |> ignore

        |   ReTweet(username,password,tweetData) -> 

            ///////
            let mutable fromSend:ResponseType = {Status=""; Data=""}
            fromSend <- mailbox.Self <? Send(username,password,tweetData,true,mailbox.Sender())
            let temp = "[retweet]" + (fromSend).Data
            let res:ResponseType = {Status="Success"; Data=temp}
            /////// 
            // let mutable res:ResponseType = {Status=""; Data=""}
            mailbox.Self <! Send(username,password,tweetData,true,mailbox.Sender())
            // mailbox.Sender() <? twitter.ReTweet  username password tweetData |> ignore

        |   Querying(username,password ) -> 
            ///////
            let mutable auth = false
            if not (usernameToUserObjMap.ContainsKey(username)) then
                printfn "%s" "[Authentication][Error]: Username not found"
            else
                let user = usernameToUserObjMap.[username]
                if user.Password = password then
                    auth <- true
            let mutable res:ResponseType = {Status=""; Data=""}
            if not (auth) then
                res <- {Status="Error"; Data="QueryTweets: Username & password do not match"}
                //res <- "[QueryTweets][Error]: Username & password do not match"
            else
                let mutable user : User = Unchecked.defaultof<User>
                if not (usernameToUserObjMap.ContainsKey(username)) then
                    printfn "%s" "[FetchUserObject][Error]: Username not found"
                else
                    user <- usernameToUserObjMap.[username]
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
            if not (hashtagToTweetMap.ContainsKey(hashtag)) then
                res <- {Status="Error"; Data="QueryHashTags: No Hashtag with given String found"}
                //res <- "[QueryHashTags][Error]: No Hashtag with given String found"
            else
                let res1 = hashtagToTweetMap.[hashtag] |>  List.map(fun x->x.ToString()) |> String.concat "\n"
                res <- {Status="Success"; Data= "\n" + res1}
            // res
            mailbox.Sender() <? res |>ignore
            ////////
            // mailbox.Sender() <? twitter.QueryHashTag  queryhashtag |> ignore

        |   QueryAt(mention) -> 
            //////
            let mutable res:ResponseType = {Status=""; Data=""}
            if not (mentionsToTweetMap.ContainsKey(mention)) then
                res <- {Status="Error"; Data="QueryMentions: No mentions are found for the given user"}
            else
                let res1 = mentionsToTweetMap.[mention] |>  List.map(fun x->x.ToString()) |> String.concat "\n"
                res <- {Status="Success"; Data= "\n" + res1}
            // res
            mailbox.Sender() <? res |>ignore
            //////
            // mailbox.Sender() <? twitter.QueryMention  at |> ignore
        
        
        | _ ->  failwith "Invalid Operation "
        return! loop()
    }
    loop ()

// let GlobalActor (mailbox: Actor<_>) =
//     let rec loop () = actor {
//         let! msg = mailbox.Receive ()
//         match msg with
//         |   Register(username,password,webSocket) ->
//             if username = "" then
//                 return! loop()
//             mailbox.Sender() <? twitter.Register username password webSocket|> ignore

//         |   Login(username,password,webSocket) ->
//             if username = "" then
//                 return! loop()
//             mailbox.Sender() <? twitter.Login username password webSocket|> ignore

//         |   Logout(username,password) ->
//             mailbox.Sender() <? twitter.Logout username password |> ignore

//         |   Send(username,password,tweetData,false) -> 
//             mailbox.Sender() <? twitter.SendTweet username password tweetData false |> ignore

//         |   Subscribe(username,password,subsribeUsername) -> 
//             mailbox.Sender() <? twitter.Subscribe username password subsribeUsername |> ignore

//         |   ReTweet(username,password,tweetData) -> 
//             mailbox.Sender() <? twitter.ReTweet  username password tweetData |> ignore

//         |   Querying(username,password ) -> 
//             mailbox.Sender() <? twitter.QueryTweetsSubscribed  username password |> ignore

//         |   QueryHashTag(queryhashtag) -> 
//             mailbox.Sender() <? twitter.QueryHashTag  queryhashtag |> ignore

//         |   QueryAt(at) -> 
//             mailbox.Sender() <? twitter.QueryMention  at |> ignore
        
        
//         | _ ->  failwith "Invalid Operation "
//         return! loop()
//     }
//     loop ()

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
    | SendOp of MessageType* WebSocket

let ServerActor (mailbox: Actor<ServerActorMessage>) = 
    let rec loop () = actor {        
        let! msg = mailbox.Receive ()
        let sender = mailbox.Sender()
        
        match msg  with
        |   SendOp(msg,webSocket) ->
            
            printfn "%A" msg.OperationName
            let mutable serverOperation= msg.OperationName
            let mutable username=msg.UserName
            let mutable password=msg.Password
            let mutable subsribeUsername=msg.SubscribeUserName
            let mutable tweetData=msg.TweetData
            let mutable queryhashtag=msg.Queryhashtag
            let mutable at=msg.QueryAt
            let mutable task = globalActor <? Register("","",webSocket)
            if serverOperation= "reg" then
                printfn "[Register] username:%s password: %s" username password
                task <- globalActor <? Register(username,password,webSocket)
            if serverOperation= "login" then
                printfn "[Login] username:%s password: %s" username password
                task <- globalActor <? Login(username,password,webSocket)
            else if serverOperation= "send" then
                printfn "[send] username:%s password: %s tweetData: %s" username password tweetData
                task <- globalActor <? Send(username,password,tweetData,false,mailbox.Self)
            else if serverOperation= "subscribe" then
                printfn "[subscribe] username:%s password: %s following username: %s" username password subsribeUsername
                task <- globalActor <? Subscribe(username,password,subsribeUsername )
            else if serverOperation= "querying" then
                printfn "[querying] username:%s password: %s" username password
                task <- globalActor <? Querying(username,password )
            else if serverOperation= "retweet" then
                printfn "[retweet] username:%s password: %s tweetData: %s" username password (tweetIdToTweetMap.[tweetData].Text)
                task <- globalActor <? ReTweet(username,password,(tweetIdToTweetMap.[tweetData].Text))
                // printfn "[retweet] username:%s password: %s tweetData: %s" username password (twitter.GetTweetIdToTweetMap().[tweetData].Text)
                // task <- globalActor <? ReTweet(username,password,(twitter.GetTweetIdToTweetMap().[tweetData].Text))
            else if serverOperation= "@" then
                printfn "[@mention] %s" at
                task <- globalActor <? QueryAt(at )
            else if serverOperation= "#" then
                printfn "[#Hashtag] %s: " queryhashtag
                task <- globalActor <? QueryHashTag(queryhashtag )
            else if serverOperation= "logout" then
                task <- globalActor <? Logout(username,password)
            let response: ResponseType = Async.RunSynchronously (task, 1000)
            sender <? response |> ignore
            printfn "[Result]: [%s] : %s " response.Status response.Data
            return! loop()     
    }
    loop ()
let serverActor = spawn system "ServerActor" ServerActor

serverActor <? "" |> ignore
printfn "*****************************************************" 
printfn "Starting Twitter Server!! ...  " 
printfn "*****************************************************"

// Console.ReadLine() |> ignore

// Start of Websocket code



let ws (webSocket : WebSocket) (context: HttpContext) =
  socket {
    // if `loop` is set to false, the server will stop receiving messages
    let mutable loop = true

    while loop do
      // the server will wait for a message to be received without blocking the thread
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
        if serverOperation = "send" then
            // let user = twitter.GetUserMap().[username]
            let user = usernameToUserObjMap.[username]
            let isRetweet = false
            let tweet = Tweet(DateTime.Now.ToFileTimeUtc() |> string, tweetData, isRetweet)
            for subUser in user.GetFollowers() do
                        if subUser.IsLoggedIn() then
                            
                            printfn "Sending message to %s %A" (subUser.ToString()) (subUser.GetSocket())
                            let byteResponse =
                                  (string("RecievedTweet,"+tweet.Text))
                                  |> System.Text.Encoding.ASCII.GetBytes
                                  |> ByteSegment
                            
                            do! subUser.GetSocket().send Text byteResponse true 

        if serverOperation = "retweet" then
            // let user = twitter.GetUserMap().[username]
            let user = usernameToUserObjMap.[username]
            let isRetweet = true
            let tweet = Tweet(DateTime.Now.ToFileTimeUtc() |> string, tweetIdToTweetMap.[tweetData].Text, isRetweet)
            // let tweet = Tweet(DateTime.Now.ToFileTimeUtc() |> string, twitter.GetTweetIdToTweetMap().[tweetData].Text, isRetweet)
            for subUser in user.GetFollowers() do
                        if subUser.IsLoggedIn() then
                            let byteResponse =
                                  (string("[Retweet] RecievedTweet,"+tweet.Text))
                                  |> System.Text.Encoding.ASCII.GetBytes
                                  |> ByteSegment
                            
                            do! subUser.GetSocket().send Text byteResponse true                 
        //


        let mutable task = serverActor <? SendOp(json,webSocket)
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
  let task = serverActor <? SendOp(serverJson,Unchecked.defaultof<WebSocket>)
  let response: ResponseType = Async.RunSynchronously (task, 1000)
  if response.Status = "Success" then
    OK (sprintf "Tweets: %s" response.Data) 
  else
    NOT_FOUND (sprintf "Error: %s" response.Data))

let handleQueryHashtags hashtag = request (fun r ->
  let serverJson: MessageType = {OperationName = "#"; UserName = ""; Password = ""; SubscribeUserName = ""; TweetData = ""; Queryhashtag = "#"+hashtag; QueryAt = ""} 
  let task = serverActor <? SendOp(serverJson,Unchecked.defaultof<WebSocket>)
  let response = Async.RunSynchronously (task, 1000)
  if response.Status = "Success" then
    OK (sprintf "Tweets: %s" response.Data) 
  else
    NOT_FOUND (sprintf "Error: %s" response.Data))

let handleQueryMentions mention = request (fun r ->
  let serverJson: MessageType = {OperationName = "@"; UserName = ""; Password = ""; SubscribeUserName = ""; TweetData = ""; Queryhashtag = ""; QueryAt = "@"+mention} 
  let task = serverActor <? SendOp(serverJson,Unchecked.defaultof<WebSocket>)
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
    let task = serverActor <? SendOp(serverJson,Unchecked.defaultof<WebSocket>)
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


//let sendTweet (body : SendTweetType) = r

let app : WebPart = 
  choose [
    path "/websocket" >=> handShake ws
    path "/websocketWithSubprotocol" >=> handShakeWithSubprotocol (chooseSubprotocol "test") ws
    //path "/websocketWithError" >=> handShake wsWithErrorHandling
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
// let main _ =
  startWebServer { defaultConfig with logger = Targets.create Verbose [||] } app
//   0






