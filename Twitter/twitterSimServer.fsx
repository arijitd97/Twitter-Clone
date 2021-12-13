// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp
#if INTERACTIVE
#r "nuget: Akka.FSharp"
#r "nuget: Akka.Remote"
#endif

open System
open Akka.FSharp
open Akka.Actor
open System.Threading
open Akka.Configuration
open System.IO
open System.Text



type ShowFeedActorMessageTypes =
    | InitialiseShowFeedActor of IActorRef
    | UpdateFeed of string * string
    | ShowUserFeed of string 
    | Retweet of string * DateTime         //To print user's feed

type TweetActorMessageTypes =
    | InitialiseTweetActor of IActorRef * IActorRef * IActorRef
    | RegisterTweet of string * string * DateTime
    | DisplayTweet of string * seq<string>
    | ShowTweetServer

type HashtagActorMessageTypes =
    | InitialiseHashtagActor of IActorRef
    | ParseHashtag of string * string
    | QueryHashTag of string * string * DateTime

type MentionActorMessageTypes =
    | InitialiseMentionActor of IActorRef * IActorRef
    | ParseMention of string * string
    | QueryMention of string * DateTime

type UserActorMessageTypes =
    | InitialiseUserActor of IActorRef
    | RegisterUser of string * DateTime
    | Online of string
    | Offline of string
    | CallUpdateFeed of string * string
    | AddFollowers of string * string * DateTime
    | Display

let serverIP = fsi.CommandLineArgs.[1] |> string
let path = "Stats.txt"
let mutable firstRegisterTime = DateTime.Now



// Configuration
let configuration = 
    ConfigurationFactory.ParseString(
        sprintf @"akka {            
            stdout-loglevel : DEBUG
            loglevel : ERROR
            actor {
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
            }
            remote.helios.tcp {
                transport-protocol = tcp
                port = 8776
                hostname = %s
            }
        }" serverIP)


let system = ActorSystem.Create("TwitterServer", configuration)

let ShowFeedActor(mailbox: Actor<'a>) =
         
    let mutable userFeedMap = Map.empty  //<uid,list<tweetstring>

    let mutable totalRetweetTime = 0.0
    let mutable avgTime = 0.0
    let mutable retweetCount=0.0
    let  mutable first=DateTime.Now

    let mutable userActor = null
    let rec loop()= actor{
        let! msg = mailbox.Receive()
        match msg with
        | InitialiseShowFeedActor userActorRef ->
            userActor <- userActorRef

        | UpdateFeed (subsId, tweetString) ->
            if not (userFeedMap.ContainsKey subsId) then
                userFeedMap <- Map.add subsId Set.empty userFeedMap
            userFeedMap <- userFeedMap.Add(subsId,userFeedMap.[subsId].Add(tweetString))

        | ShowUserFeed userId ->

            if (userFeedMap.ContainsKey userId) then
                for tweet in userFeedMap.[userId] do
                    printfn "User feed for %s is %A" userId userFeedMap.[userId]

            else printfn "User %s : Your feed is empty(userFeedMap)" userId
        
        | Retweet (userId,startTime) ->
            retweetCount <- retweetCount + 1.0
            if retweetCount = 1.0 then
                first <- startTime
            if (userFeedMap.ContainsKey userId) then
                let tweetList = Set.toList userFeedMap.[userId]
                let tweetString = tweetList.[Random().Next(0,tweetList.Length)]
                userActor <! CallUpdateFeed (userId, tweetString)
            else
                printfn " No feed, hence no retweet for userId :  %s " userId
            
            let timestamp = DateTime.Now
            totalRetweetTime <- (timestamp.Subtract first).TotalMilliseconds
            avgTime <- totalRetweetTime/retweetCount;
            let s = sprintf "%f|%f" avgTime retweetCount
            mailbox.Sender() <! ("PerfTester","Retweet",s,DateTime.Now)
           
        return! loop()
    }
    loop() 


let TweetActor(mailbox: Actor<'a>) =
    let mutable first = DateTime.Now
    let mutable totalTweetTime = 0.0
    let mutable tweetCount = 0.0
    let mutable avgTime = 0.0
    let mutable tweetList = Map.empty       //<tweetid,tweetstring>
    let mutable userTweets = Map.empty      //<uid,list<tweetid>>
    let mutable userActor = null
    let mutable hashtagActor = null
    let mutable mentionActor = null
    let rec loop()= actor{
        let! msg = mailbox.Receive()
        match msg with
        | InitialiseTweetActor (userActorRef, hashtagActorRef, mentionActorRef) ->
            userActor <- userActorRef
            hashtagActor <- hashtagActorRef
            mentionActor <- mentionActorRef
        
        | RegisterTweet (userId, tweetString, startTime) ->
            tweetCount <- tweetCount + 1.0
            if tweetCount = 1.0 then
                first <- startTime
            let tweetId = string(tweetCount)
            tweetList <- tweetList.Add(tweetId,tweetString)

            let timestamp = DateTime.Now
            totalTweetTime <- (timestamp.Subtract first).TotalMilliseconds
            avgTime <- totalTweetTime/tweetCount;
            let s = sprintf "%f|%f" avgTime tweetCount
            mailbox.Sender() <! ("PerfTester","Tweet",s,DateTime.Now)

            if not (userTweets.ContainsKey userId) then
                userTweets <- Map.add userId Set.empty userTweets
            userTweets <- userTweets.Add(userId,userTweets.[userId].Add(tweetId))
            hashtagActor <! ParseHashtag (tweetId,tweetString)
            mentionActor <! ParseMention (tweetId,tweetString)
            userActor <! CallUpdateFeed(userId, tweetString)


        | DisplayTweet (userId,tweetIdList) ->
            for tweetId in tweetIdList do
                printfn "Message for User %s : %s" userId tweetList.[tweetId]

        | ShowTweetServer ->

            printfn "In server Showing Tweet server"
            for tweets in tweetList do
                printfn "Tweet Id %s - %A" tweets.Key tweets.Value

            for user in userTweets do
                printfn "UserId %s - %A" user.Key user.Value
            
        return! loop()
    }
    loop()

let MentionActor(mailbox: Actor<'a>) =
    let mutable mentionMap = Map.empty    //<uid,list<tweetid>>

    let mutable tweetActor = null
    let mutable showFeedActor = null

    let mutable totalQueryMentionTime = 0.0
    let mutable avgTime = 0.0
    let mutable queryCount=0.0
    let mutable first = DateTime.Now

    let rec loop()= actor{
        let! msg = mailbox.Receive()
        match msg with
        | InitialiseMentionActor (tweetActorRef, showFeedActorRef) ->
            tweetActor <- tweetActorRef
            showFeedActor <- showFeedActorRef

        | ParseMention (tweetId,tweetString) ->
            let words = tweetString.Split ' '
            for word in words do
                if word.[0] = '@' then
                    let parsedMention = word.[1..(word.Length-1)]
                    if not (mentionMap.ContainsKey parsedMention) then
                        mentionMap <- Map.add parsedMention Set.empty mentionMap

                    mentionMap <- mentionMap.Add(parsedMention,mentionMap.[parsedMention].Add(tweetId))
                    showFeedActor <! UpdateFeed (parsedMention,tweetString)


        | QueryMention (userId, startTime) ->
            queryCount <- queryCount + 1.0
            if queryCount = 1.0 then
                first <- startTime
            if mentionMap.ContainsKey userId then
                tweetActor <! DisplayTweet (userId,mentionMap.[userId])      
            else printfn "Message for User %s : You have not been mentioned by anyone" userId

            let timestamp = DateTime.Now
            totalQueryMentionTime <- (timestamp.Subtract first).TotalMilliseconds
            avgTime <- totalQueryMentionTime/queryCount
            let s = sprintf "%f|%f" avgTime queryCount
            mailbox.Sender() <! ("PerfTester","QueryMention",s,DateTime.Now)
            
        return! loop()
    }
    loop()

let HashtagActor(mailbox: Actor<'a>) =
    let mutable hashtagMap = Map.empty      //<hashtag,list<tweetid>>
    let mutable totalQueryHashtagTime = 0.0
    let mutable avgTime = 0.0
    let mutable queryCount=0.0
    let mutable tweetActor = null

    let mutable first = DateTime.Now
    
    
    let rec loop()= actor{
        let! msg = mailbox.Receive()
        match msg with
        | InitialiseHashtagActor tweetActorRef ->
            tweetActor <- tweetActorRef
        
        | ParseHashtag (tweetId,tweetString) ->
            let words = tweetString.Split ' '
            for word in words do
                if word.[0] = '#' then
                    let parsedHashtag = word.[1..(word.Length-1)]
                    if not (hashtagMap.ContainsKey parsedHashtag) then
                        hashtagMap <- Map.add parsedHashtag Set.empty hashtagMap

                    hashtagMap <- hashtagMap.Add(parsedHashtag,hashtagMap.[parsedHashtag].Add(tweetId))


        | QueryHashTag (userId, hashtag, startTime) ->
            queryCount <- queryCount + 1.0
            if queryCount = 1.0 then
                first <- startTime
            if hashtagMap.ContainsKey hashtag then
                tweetActor <! DisplayTweet (userId,hashtagMap.[hashtag])
            else printfn "Message for User %s : No hashtag called %s" userId hashtag

            let timestamp = DateTime.Now
            totalQueryHashtagTime <- (timestamp.Subtract first).TotalMilliseconds
            avgTime <- totalQueryHashtagTime/queryCount;
            let s = sprintf "%f|%f" avgTime queryCount
            mailbox.Sender() <! ("PerfTester","QueryHashtag",s,DateTime.Now)

            
        return! loop()
    }
    loop()

let UserActor(mailbox: Actor<'a>) =
    
    let mutable first= DateTime.Now
    let mutable users = Map.empty                   //<uid,boolean(active,inactive)>>
    let mutable subscribersMap = Map.empty          //<uid,list<followers>>
    let mutable totalRegisterTime = 0.0
    let mutable avgTime = 0.0
    let mutable totalRegRequests = 0.0
    let mutable totalFollowTime = 0.0  
    let mutable totalFollowRequests = 0.0   
    let mutable followAvgTime=0.0     
    let mutable showFeedActor = null
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with
        | InitialiseUserActor showFeedActorRef ->
            showFeedActor <- showFeedActorRef

        | RegisterUser (userId, startTime) ->
            totalRegRequests <- totalRegRequests + 1.0
            if totalRegRequests = 1.0 then
                firstRegisterTime <- startTime

            users <- Map.add userId true users
            // printfn "%A" users

            let timestamp = DateTime.Now
            totalRegisterTime <- (timestamp.Subtract firstRegisterTime).TotalMilliseconds
            avgTime <- totalRegisterTime/totalRegRequests;
            // let s = sprintf "%f|%f" avgTime totalRegRequests
            let s = sprintf "%f|%f" avgTime totalRegRequests
            mailbox.Sender() <! ("PerfTester","Register",s,DateTime.Now)

            subscribersMap <- Map.add userId Set.empty subscribersMap


        | CallUpdateFeed (userId, tweetString) ->
            if subscribersMap.ContainsKey userId then
                for subs in subscribersMap.[userId] do
                    showFeedActor <! UpdateFeed(subs, tweetString)

        | AddFollowers(userId, followingId, startTime) ->
            totalFollowRequests <- totalFollowRequests + 1.0
            if totalFollowRequests = 1.0 then
                first <- startTime
            if subscribersMap.ContainsKey followingId then
                subscribersMap <- subscribersMap.Add(followingId,subscribersMap.[followingId].Add(userId))

                
            let timestamp = DateTime.Now
            totalFollowTime <- (timestamp.Subtract first).TotalMilliseconds
            followAvgTime <- totalFollowTime/totalFollowRequests;
            let s = sprintf "%f|%f" followAvgTime totalFollowRequests
            mailbox.Sender() <! ("PerfTester","Follow",s,DateTime.Now)
        
        | Display ->
            printfn "Users:"
            printfn "%A" users

            printfn "Subscribers:" 
            printfn "%A" subscribersMap

            showFeedActor <! ShowUserFeed
 
        return! loop()
    }
    loop()
let Server(mailbox: Actor<'a>) =
    let maxRequests = 100000
    let mutable userActor = null
    let mutable tweetActor = null
    let mutable hashtagActor = null
    let mutable mentionActor = null
    let mutable showFeedActor = null
    let mutable requestMap = Map.empty           //Holds the requests performance(average time) statistics
    let mutable requestCountMap = Map.empty
    let rec loop()= actor{
        let! (msg:obj) = mailbox.Receive()
        let (mtype,_,_,_) : Tuple<string,string,string,DateTime> = downcast msg
        match mtype with
        | "SetParameters" ->
            userActor <- spawn system ("UserActor") UserActor
            tweetActor <- spawn system ("TweetActor") TweetActor
            hashtagActor <- spawn system ("HashtagActor") HashtagActor
            mentionActor <- spawn system ("MentionsActor") MentionActor
            showFeedActor <- spawn system ("ShowFeedActor") ShowFeedActor

            userActor <! InitialiseUserActor (showFeedActor)        //Initialising Actors in UserActor
            hashtagActor <! InitialiseHashtagActor (tweetActor)
            mentionActor <! InitialiseMentionActor (tweetActor, showFeedActor)
            showFeedActor <! InitialiseShowFeedActor (userActor)
            tweetActor <! InitialiseTweetActor (userActor, hashtagActor, mentionActor)

            requestMap <- Map.add "Register" "0" requestMap
            requestMap <- Map.add "Follow" "0" requestMap
            requestMap <- Map.add "Tweet" "0" requestMap
            requestMap <- Map.add "Retweet" "0" requestMap
            requestMap <- Map.add "QueryHashtag" "0" requestMap
            requestMap <- Map.add "QueryMention" "0" requestMap
            requestCountMap <- Map.add "Register" 0 requestCountMap
            requestCountMap <- Map.add "Follow" 0 requestCountMap
            requestCountMap <- Map.add "Tweet" 0 requestCountMap
            requestCountMap <- Map.add "Retweet" 0 requestCountMap
            requestCountMap <- Map.add "QueryHashtag" 0 requestCountMap
            requestCountMap <- Map.add "QueryMention" 0 requestCountMap
            printfn "\n\nTotal requests after which server will stop is set to %i." maxRequests
            printfn "Initialisation complete. Please run client."


        | "Register"  ->
            let (_,userId,_,startTime) : Tuple<string,string,string,DateTime> = downcast msg
            userActor <! RegisterUser (userId, startTime)

        | "Tweet" ->
            let (_,userId,tweetString,startTime) : Tuple<string,string,string,DateTime> = downcast msg
            tweetActor <! RegisterTweet (userId,tweetString, startTime)


        | "Retweet" ->
            let (_,userId,_,startTime) : Tuple<string,string,string,DateTime> = downcast msg
            showFeedActor <! Retweet (userId, startTime)

        | "ShowFeed" ->
            let (_,userId,_,_) : Tuple<string,string,string,DateTime> = downcast msg
            showFeedActor <! ShowUserFeed (userId) 

        | "Subscribe" ->
            let (_,userId,followingId, startTime) : Tuple<string,string,string,DateTime> = downcast msg
            userActor <! AddFollowers(userId, followingId, startTime)

        | "SearchHashTag" ->
            let (_,userId,hashtag,startTime): Tuple<string,string,string,DateTime> = downcast msg
            hashtagActor <! QueryHashTag (userId,hashtag, startTime)

        | "SearchMention" ->
            let (_,userId,_,startTime) : Tuple<string,string,string,DateTime> = downcast msg
            mentionActor <! QueryMention (userId, startTime)

        | "PerfTester" ->
            let (_,service,var,_) : Tuple<string,string,string,DateTime> = downcast msg
            if service <> "" then
                let words = var.Split '|'
                let totalTime = words.[0]
                let count = words.[1]|>float|>int
                let avgTime = (float(totalTime)/float(count))|>string
                if requestMap.ContainsKey service then
                    requestMap <- Map.remove service requestMap
                requestMap <- Map.add service totalTime requestMap
                if requestCountMap.ContainsKey service then
                    requestCountMap <- Map.remove service requestCountMap
                requestCountMap <- Map.add service count requestCountMap

            let total = requestCountMap |> Seq.sumBy(fun item -> item.Value)
            if total = maxRequests then
                mailbox.Self <! ("PrintStats","","",DateTime.Now)


        | "DisplayServer" ->
            let (_,_,_,_) : Tuple<string,string,string,DateTime> = downcast msg
            printfn "DisplayServer hit"
            userActor <! Display
            Thread.Sleep(5000)
            printfn "Showing Tweet server"
            tweetActor <! ShowTweetServer
        
        | "PrintStats" ->
            let (_,_,_,_) : Tuple<string,string,string,DateTime> = downcast msg
            let total = requestCountMap |> Seq.sumBy(fun item -> item.Value)
            let totalTime = (DateTime.Now.Subtract firstRegisterTime).TotalSeconds
            File.WriteAllText(path, sprintf "Performance Statistics")
            File.AppendAllText(path, (sprintf "\n\nTotal number of requests = %i" total))
            File.AppendAllText(path, (sprintf "\nTotal server uptime = %f seconds" totalTime))
            File.AppendAllText(path, sprintf "\n\nType of request - No. of requests - Average time per request(ms)")
            File.AppendAllText(path, (sprintf "\nRegister - %i - %s" (requestCountMap.["Register"]) (requestMap.["Register"])))
            File.AppendAllText(path, (sprintf "\nFollow - %i - %s" (requestCountMap.["Follow"]) (requestMap.["Follow"])))
            File.AppendAllText(path, (sprintf "\nTweet - %i - %s" (requestCountMap.["Tweet"]) (requestMap.["Tweet"])))
            File.AppendAllText(path, (sprintf "\nRetweet - %i - %s" (requestCountMap.["Retweet"]) (requestMap.["Retweet"])))
            File.AppendAllText(path, (sprintf "\nQueryHashtag - %i - %s" (requestCountMap.["QueryHashtag"]) (requestMap.["QueryHashtag"])))
            File.AppendAllText(path, (sprintf "\nQueryMention - %i - %s" (requestCountMap.["QueryMention"]) (requestMap.["QueryMention"])))
            
            printfn "Performance Statistics\n"
            printfn "Type of request - No. of requests - Avg time per request(ms)"
            printfn "Register - %i - %s" (requestCountMap.["Register"]) (requestMap.["Register"])
            printfn "Follow - %i - %s" (requestCountMap.["Follow"]) (requestMap.["Follow"])
            printfn "Tweet - %i - %s" (requestCountMap.["Tweet"]) (requestMap.["Tweet"])
            printfn "Retweet - %i - %s" (requestCountMap.["Retweet"]) (requestMap.["Retweet"])
            printfn "QueryHashtag - %i - %s" (requestCountMap.["QueryHashtag"]) (requestMap.["QueryHashtag"])
            printfn "QueryMention - %i - %s" (requestCountMap.["QueryMention"]) (requestMap.["QueryMention"])
            printfn "\nTotal Requests = %i" total
            printfn "Total server uptime = %f seconds" totalTime
            
            Environment.Exit(0)


            

        return! loop()
    }
    loop()

let serverActor = spawn system "MasterSpace" Server

serverActor <! ("SetParameters","","", DateTime.Now)

Console.ReadLine() |> ignore