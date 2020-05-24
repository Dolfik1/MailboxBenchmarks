using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using MailboxBenchmarks.Shared;

namespace MailboxBenchmarks.CSharp
{
  public class MailboxBenchmarksCSharp
  {
    public async Task CSharpLoop(int countMessages)
    {
      var s = 0;
      async Task Incr()
      {
        s += 1;
      }

      await Task.Yield();
      while (s < countMessages)
      {
        await Incr();
      }
    }
    
    public async Task MailboxProcessorCSharpTask(int countAgents, int countMessages)
    {
      async Task Loop(FSharpMailboxProcessor<Message> inbox)
      {
        while (true)
        {
          var message =
            await FSharpAsync.StartAsTask(
              inbox.Receive(FSharpOption<int>.None),
              FSharpOption<TaskCreationOptions>.None,
              FSharpOption<CancellationToken>.None);

          TaskCompletionSource<Unit> tcs = null;
          if (message.TryDone(ref tcs))
          {
            tcs.SetResult(null);
          }
        }
      }
      
      var agents = new FSharpMailboxProcessor<Message>[countAgents];
      for (var i = 0; i < countAgents; i++)
      {
        var starts =
          FSharpFunc<FSharpMailboxProcessor<Message>, FSharpAsync<Unit>>.FromConverter(
            inbox =>
              FSharpAsync.AwaitTask(Loop(inbox))
          );
        
        agents[i] = FSharpMailboxProcessor<Message>.Start(starts, FSharpOption<CancellationToken>.None);
      }

      var random = new Random();
      for (var i = 0; i < countMessages; i++)
      {
        var t = random.Next(0, countAgents - 1);
        agents[t].Post(Message.Unit);
      }
      
      var tasks = new Task<Unit>[countAgents];
      for (var i = 0; i < countAgents; i++)
      {
        var tcs = new TaskCompletionSource<Unit>();
        agents[i].Post(Message.NewDone(tcs));
        tasks[i] = tcs.Task;
      }

      await Task.WhenAll(tasks);
    }
  }
}