using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Abc.Zebus.Core;
using Abc.Zebus.Dispatch;
using Abc.Zebus.Util.Extensions;
using log4net.Core;

namespace Abc.Zebus.Scan.Pipes
{
    public class PipeInvocation : IMessageHandlerInvocation
    {
        private readonly List<Action<object>> _handlerMutations = new List<Action<object>>();
        private readonly IMessageHandlerInvoker _invoker;
        private readonly IMessage _message;
        private readonly MessageContext _messageContext;
        private readonly IList<IPipe> _pipes;
        private readonly BusMessageLogger _messageLogger = BusMessageLogger.Get<PipeInvocation>();
        
        private object[] _pipeStates;

        public PipeInvocation(IMessageHandlerInvoker invoker, IMessage message, MessageContext messageContext, IEnumerable<IPipe> pipes)
        {
            _invoker = invoker;
            _message = message;
            _messageContext = messageContext;
            _pipes = pipes.AsList();
        }

        internal IList<IPipe> Pipes
        {
            get { return _pipes; }
        }

        public IMessageHandlerInvoker Invoker
        {
            get { return _invoker; }
        }

        public IMessage Message
        {
            get { return _message; }
        }

        public MessageContext Context
        {
            get { return _messageContext; }
        }

        public void AddHandlerMutation(Action<object> action)
        {
            _handlerMutations.Add(action);
        }

        protected internal virtual void Run()
        {
            _messageLogger.LogFormat("HANDLE : {0} [{1}]", _message, _messageContext.MessageId, logLevel: Level.Debug);
            _pipeStates = BeforeInvoke();

            try
            {
                _invoker.InvokeMessageHandler(this);
            }
            catch (Exception exception)
            {
                AfterInvoke(_pipeStates, true, exception);
                throw;
            }

            AfterInvoke(_pipeStates, false, null);
        }

        private object[] BeforeInvoke()
        {
            var stateRef = new BeforeInvokeArgs.StateRef();
            var pipeStates = new object[_pipes.Count];
            for (var pipeIndex = 0; pipeIndex < _pipes.Count; ++pipeIndex)
            {
                var beforeInvokeArgs = new BeforeInvokeArgs(this, stateRef);
                _pipes[pipeIndex].BeforeInvoke(beforeInvokeArgs);
                pipeStates[pipeIndex] = beforeInvokeArgs.State;
            }
            return pipeStates;
        }

        private void AfterInvoke(object[] pipeStates, bool isFaulted, Exception exception)
        {
            if (pipeStates == null)
                throw new InvalidOperationException("Missing pipe states, did you call SetupForInvocation in your handler invoker?");

            for (var pipeIndex = _pipes.Count - 1; pipeIndex >= 0; --pipeIndex)
            {
                var afterInvokeArgs = new AfterInvokeArgs(this, pipeStates[pipeIndex], isFaulted, exception);
                _pipes[pipeIndex].AfterInvoke(afterInvokeArgs);
            }
        }

        protected internal virtual Task RunAsync()
        {
            _messageLogger.LogFormat("HANDLE : {0} [{1}]", _message, _messageContext.MessageId, logLevel: Level.Debug);
            var runTask = _invoker.InvokeMessageHandlerAsync(this);
            runTask.ContinueWith(task => AfterInvoke(_pipeStates, task.IsFaulted, task.Exception), TaskContinuationOptions.ExecuteSynchronously);

            return runTask;
        }

        IDisposable IMessageHandlerInvocation.SetupForInvocation()
        {
            if (_pipeStates == null)
                _pipeStates = BeforeInvoke();

            return MessageContext.SetCurrent(_messageContext);
        }

        IDisposable IMessageHandlerInvocation.SetupForInvocation(object messageHandler)
        {
            if (_pipeStates == null)
                _pipeStates = BeforeInvoke();

            ApplyMutations(messageHandler);

            return MessageContext.SetCurrent(_messageContext);
        }

        private void ApplyMutations(object messageHandler)
        {
            var messageContextAwareHandler = messageHandler as IMessageContextAware;
            if (messageContextAwareHandler != null)
                messageContextAwareHandler.Context = Context;

            foreach (var messageHandlerMutation in _handlerMutations)
            {
                messageHandlerMutation(messageHandler);
            }
        }
    }
}
