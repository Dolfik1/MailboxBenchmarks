using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using MailboxBenchmarks.Shared;

namespace MailboxBenchmarks.CSharp
{
  public class MailboxBenchmarksCSharp
  {
    public async ValueTask ChannelCSharp(int countAgents, int countMessages)
    {
      //var rnd = new Random(0);
      async ValueTask CreateMailbox(Channel<Message> channel)
      {
        TaskCompletionSource<Unit> tcs = null;
        while (true)
        {
          var message = await channel.Reader.ReadAsync();
          // if (rnd.Next(0, 100) < 99)
          //  await Task.Yield();
          
          if (message.TryDone(ref tcs))
          {
            tcs.SetResult(null);
          }
        }
      }

      var agents = new Channel<Message>[countAgents];
      for (var i = 0; i < agents.Length; i++)
      {
        var a = Channel.CreateUnbounded<Message>();
        agents[i] = a;
        CreateMailbox(a);
      }

      var random = new Random(countMessages);
      for (var i = 0; i < countMessages; i++)
      {
        var t = random.Next(0, countAgents - 1);
        agents[t].Writer.WriteAsync(Message.Unit);
      }

      var tasks = new Task[countAgents];
      for (var i = 0; i < countAgents; i++)
      {
        var tcs = new TaskCompletionSource<Unit>();
        agents[i].Writer.WriteAsync(Message.NewDone(tcs));
        tasks[i] = tcs.Task;
      }

      await Task.WhenAll(tasks);
    }
  }
}