// Learn more about F# at http://fsharp.org

open System
open System.Threading.Tasks
open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Configs
open BenchmarkDotNet.Diagnosers
open BenchmarkDotNet.Engines
open BenchmarkDotNet.Loggers
open BenchmarkDotNet.Running

type Message =
  | Unit
  | Done of TaskCompletionSource<unit>

[<SimpleJob(RunStrategy.Monitoring, launchCount = 1, warmupCount = 1, targetCount = 1)>]
[<MemoryDiagnoser>]
type MailboxBenchmarks() =
  
  let mutable countAgents = 0
  let mutable countMessages = 0
  
  [<Params(100_000, 1)>]
  member x.CountAgents
    with get() = countAgents
    and set(v) = countAgents <- v


  [<Params(1_000_000)>]
  member x.CountMessages
    with get() = countMessages
    and set(v) = countMessages <- v

  [<Benchmark>]
  member x.MailboxProcessorRecursion() =
    async {
      let loop (inbox: MailboxProcessor<Message>) =
        let rec loop =
          async {
            match! inbox.Receive() with
            | Unit -> ()
            | Done tcs -> tcs.SetResult(())
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
  0 // return an integer exit code
