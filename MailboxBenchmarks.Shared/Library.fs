namespace MailboxBenchmarks.Shared

open System.Threading.Tasks

type Message =
  | Unit
  | Done of TaskCompletionSource<unit>
  member x.TryDone(tcs: TaskCompletionSource<unit> byref) =
      match x with
      | Done(t) -> tcs <- t; true
      | _ -> false