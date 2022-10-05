// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace CypherNetwork.Helper;

/// <summary>
/// 
/// </summary>
public abstract class Msg {}

/// <summary>
/// 
/// </summary>
public abstract class ReceivedActor<T>
{
    protected virtual Task OnReceiveAsync(T message)
    {
        return Task.CompletedTask;
    }

    private readonly ActionBlock<T> _action;

    /// <summary>
    /// 
    /// </summary>
    protected ReceivedActor(ExecutionDataflowBlockOptions dataflowBlockOptions)
    {
        _action = new ActionBlock<T>(message =>
        {
            dynamic self = this;
            dynamic msg = message;
            self.OnReceiveAsync(msg);
        }, dataflowBlockOptions);
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="message"></param>
    protected async Task PostAsync(T message)
    {
       await _action.SendAsync(message);
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="message"></param>
    public void Post(T message)
    {
        _action.Post(message);
    }

    /// <summary>
    /// 
    /// </summary>
    protected Task Completion
    {
        get
        {
            _action.Complete();
            return _action.Completion;
        }
    }
}