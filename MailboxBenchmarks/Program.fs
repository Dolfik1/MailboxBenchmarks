// Learn more about F# at http://fsharp.org

open System
open System.Threading.Channels
open System.Threading.Tasks
open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Configs
open BenchmarkDotNet.Diagnosers
open BenchmarkDotNet.Engines
open BenchmarkDotNet.Loggers
open BenchmarkDotNet.Running
open FSharp.Control.Tasks
open MailboxBenchmarks.CSharp
open MailboxBenchmarks.Shared
open Hopac

#nowarn "42"

[<CsvExporter>]
[<CsvMeasurementsExporter>]
[<SimpleJob(RunStrategy.Monitoring, launchCount = 1, warmupCount = 1, targetCount = 1)>]
[<MemoryDiagnoser>]
[<RPlotExporter>]
type MailboxBenchmarks() =
  
  let mutable countAgents = 0
  let mutable countMessages = 0
  
  [<Params(4, 10_000, 100_000)>]
  member x.CountAgents
    with get() = countAgents
    and set(v) = countAgents <- v


  [<Params(1_000_000)>]
  member x.CountMessages
    with get() = countMessages
    and set(v) = countMessages <- v
  
  [<Benchmark>]
  member x.Hopac() =
    async {
      let loop (mailbox: Hopac.Mailbox<_>) =
        job {
          match! mailbox with
          | Message.Done tcs ->
            tcs.SetResult(())
          | _ -> ()
        }
        |> Job.foreverServer
        |> start

      let agents =
        [|
          for _ in 1 .. x.CountAgents do
            let processor = Hopac.Mailbox()
            loop processor
            processor
        |]
      
      let random = Random x.CountMessages
      
      for _ = 1 to x.CountMessages do
        let t = random.Next(0, x.CountAgents - 1)
        let mb = agents.[t]
        Mailbox.send mb Message.Unit |> start
        
      let tcs =
        [|
          for i in 0 .. x.CountAgents - 1 do
            let t = TaskCompletionSource()
            Mailbox.send agents.[i] (Message.Done t) |> start
            
            t.Task
        |]
      
      do! Task.WhenAll(tcs) |> Async.AwaitTask |> Async.Ignore
    } |> Async.StartAsTask
    
  [<Benchmark>]
  member x.MailboxProcessorRecursion() =
    async {
      let loop (inbox: MailboxProcessor<Message>) =
        let rec loop =
          let proc msg =
            match msg with
            | Unit -> ()
            | Done tcs -> tcs.SetResult(())
            
          async {
            let! msg = inbox.Receive()
            proc msg
            
            return! loop
          }
        loop
        
      let agents =
        [|
          for _ in 1 .. x.CountAgents do
            MailboxProcessor.Start(loop)
        |]
      
      let random = Random(x.CountMessages)
      for _ in 1 .. x.CountMessages do
        let t = random.Next(0, x.CountAgents - 1)
        agents.[t].Post(Message.Unit)
       
      let tcs =
        [|
          for i in 0 .. x.CountAgents - 1 do
            let t = TaskCompletionSource()
            agents.[i].Post(Message.Done t)
            t.Task
        |]
      
      do! Task.WhenAll(tcs) |> Async.AwaitTask |> Async.Ignore
    } |> Async.StartAsTask
  
  [<Benchmark>]
  member x.ChannelCSharp() =
    MailboxBenchmarksCSharp().ChannelCSharp(x.CountAgents, x.CountMessages);
  
  [<Benchmark>]
  member x.Channel() =
    task {
      let createMailbox () =
        let channel = Channel.CreateUnbounded<Message>()
        
        task {
          while true do
            match! channel.Reader.ReadAsync() with
            | Message.Done tcs -> tcs.SetResult()
            | _ -> ()
        } |> ignore
        
        channel
        
      let agents = [| for _ in 1 .. x.CountAgents do createMailbox () |]
      
      let random = Random(x.CountMessages)
      for _ in 1 .. x.CountMessages do
        let t = random.Next(0, x.CountAgents - 1)
        agents.[t].Writer.WriteAsync(Message.Unit) |> ignore
       
      let tcs =
        [|
          for i in 0 .. x.CountAgents - 1 do
            let t = TaskCompletionSource()
            agents.[i].Writer.WriteAsync(Message.Done t) |> ignore
            t.Task
        |]
      
      let! _ = Task.WhenAll(tcs)
      ()
    }
  
  [<Benchmark>]
  member x.MailboxProcessorLoop() =
    async {
      let loop (inbox: MailboxProcessor<Message>) =
        async {
          while true do
            match! inbox.Receive() with
            | Unit -> ()
            | Done tcs -> tcs.SetResult(())
        }
        
      let agents = [| for _ in 1 .. x.CountAgents do MailboxProcessor.Start(loop) |]
      
      let random = Random(x.CountMessages)
      for _ in 1 .. x.CountMessages do
        let t = random.Next(0, x.CountAgents - 1)
        agents.[t].Post(Message.Unit)
       
      let tcs =
        [|
          for i in 0 .. x.CountAgents - 1 do
            let t = TaskCompletionSource()
            agents.[i].Post(Message.Done t)
            t.Task
        |]
      
      do! Task.WhenAll(tcs) |> Async.AwaitTask |> Async.Ignore
    } |> Async.StartAsTask

[<EntryPoint>]
let main argv =
  let config =
    ManualConfig()
      .AddDiagnoser(MemoryDiagnoser.Default)
      .AddDiagnoser(ThreadingDiagnoser.Default)
      .AddLogger(ConsoleLogger.Default)
      
  BenchmarkRunner.Run<MailboxBenchmarks>() |> ignore
  // let mb = MailboxBenchmarks()
  // mb.CountMessages <- 1_000_000
  // mb.CountAgents <- 1
  // mb.Channel().Wait()
  0 // return an integer exit code
