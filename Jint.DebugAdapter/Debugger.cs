﻿using System.Threading.Channels;
using Esprima;
using Esprima.Ast;
using Jint.DebugAdapter.Breakpoints;
using Jint.Native;
using Jint.Runtime.Debugger;

namespace Jint.DebugAdapter
{
    public delegate void DebugLogMessageEventHandler(string message, DebugInformation info);
    public delegate void DebugPauseEventHandler(PauseReason reason, DebugInformation info);
    public delegate void DebugEventHandler();

    public class Debugger
    {
        private enum DebuggerState
        {
            WaitingForUI,
            Entering,
            Running,
            Pausing,
            Stepping,
            Terminating
        }

        private readonly Dictionary<string, ScriptInfo> scriptInfoBySourceId = new();
        private readonly Engine engine;
        private readonly ManualResetEvent waitForContinue = new(false);
        private readonly CancellationTokenSource cts = new();
        private StepMode nextStep;
        private DebuggerState state;

        public bool PauseOnEntry { get; set; }
        public bool IsAttached { get; private set; }
        public DebugInformation CurrentDebugInformation { get; private set; }
        public Engine Engine => engine;

        public event DebugLogMessageEventHandler LogPoint;
        public event DebugPauseEventHandler Stopped;
        public event DebugEventHandler Continued;
        public event DebugEventHandler Cancelled;
        public event DebugEventHandler Done;

        public Debugger(Engine engine)
        {
            this.engine = engine;
        }

        public void Execute(string sourceId, string source, bool debug)
        {
            var ast = PrepareScript(sourceId, source);
            Task.Run(() =>
            {
                if (debug)
                {
                    Attach();
                }
                try
                {
                    // Pause the engine thread, to wait for the debugger UI
                    state = DebuggerState.WaitingForUI;
                    PauseThread();

                    engine.Execute(ast);
                    Done?.Invoke();
                }
                finally
                {
                    Detach();
                }
            }, cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled)
                {
                    Cancelled?.Invoke();
                }
                if (t.IsFaulted)
                {
                    // TODO: Better handling
                    throw t.Exception.InnerExceptions[0];
                }
            });
        }

        public ScriptInfo GetScriptInfo(string id)
        {
            return scriptInfoBySourceId.GetValueOrDefault(id);
        }

        public JsValue Evaluate(string expression)
        {
            return engine.DebugHandler.Evaluate(expression);
        }

        public JsValue Evaluate(Script expression)
        {
            return engine.DebugHandler.Evaluate(expression);
        }

        /// <summary>
        /// Terminates script execution
        /// </summary>
        public void Terminate()
        {
            cts.Cancel();
            state = DebuggerState.Terminating;
            waitForContinue.Set();
        }

        public void StepOver()
        {
            nextStep = StepMode.Over;
            waitForContinue.Set();
        }

        public void StepInto()
        {
            nextStep = StepMode.Into;
            waitForContinue.Set();
        }

        public void StepOut()
        {
            nextStep = StepMode.Out;
            waitForContinue.Set();
        }

        public void Run()
        {
            state = DebuggerState.Running;
            nextStep = StepMode.None;
            waitForContinue.Set();
        }

        public void Pause()
        {
            state = DebuggerState.Pausing;
        }

        public void ClearBreakpoints()
        {
            engine.DebugHandler.BreakPoints.Clear();
        }

        public Position SetBreakpoint(string sourceId, Position position, string condition = null, string hitCondition = null, string logMessage = null)
        {
            var info = GetScriptInfo(sourceId);
            position = info.FindNearestBreakpointPosition(position);

            engine.DebugHandler.BreakPoints.Set(new ExtendedBreakPoint(
                sourceId, position.Line, position.Column, condition, hitCondition, logMessage));
            return position;
        }

        public void NotifyUIReady()
        {
            state = DebuggerState.Entering;
            waitForContinue.Set();
        }

        private void Attach()
        {
            if (IsAttached)
            {
                throw new InvalidOperationException($"Attempt to attach debugger when already attached.");
            }
            IsAttached = true;
            engine.DebugHandler.Break += DebugHandler_Break;
            engine.DebugHandler.Step += DebugHandler_Step;
        }

        private void Detach()
        {
            if (!IsAttached)
            {
                return;
            }
            engine.DebugHandler.Break -= DebugHandler_Break;
            engine.DebugHandler.Step -= DebugHandler_Step;
            IsAttached = false;
        }

        private Script PrepareScript(string sourceId, string source)
        {
            var parser = new JavaScriptParser(source, new ParserOptions(sourceId) { Tokens = true, AdaptRegexp = true, Tolerant = true });
            var ast = parser.ParseScript();
            RegisterScriptInfo(sourceId, ast);
            return ast;
        }

        private void RegisterScriptInfo(string id, Script ast)
        {
            scriptInfoBySourceId.Add(id, new ScriptInfo(ast));
        }

        private StepMode DebugHandler_Step(object sender, DebugInformation e)
        {
            cts.Token.ThrowIfCancellationRequested();

            if (!IsAttached)
            {
                return StepMode.None;
            }

            HandleBreakpoint(e);

            switch (state)
            {
                case DebuggerState.WaitingForUI:
                    throw new InvalidOperationException("Debugger should not be stepping while waiting for UI");

                case DebuggerState.Entering:
                    if (!PauseOnEntry)
                    {
                        state = DebuggerState.Running;
                        return StepMode.None;
                    }
                    state = DebuggerState.Stepping;
                    return OnPause(PauseReason.Entry, e);

                case DebuggerState.Running:
                    return StepMode.None;

                case DebuggerState.Pausing:
                    state = DebuggerState.Stepping;
                    return OnPause(PauseReason.Pause, e);

                case DebuggerState.Stepping:
                    return OnPause(PauseReason.Step, e);

                case DebuggerState.Terminating:
                    throw new InvalidOperationException("Debugger should not be stepping while terminating");

                default:
                    throw new NotImplementedException($"Debugger state handling for {state} not implemented.");
            }

        }

        private StepMode DebugHandler_Break(object sender, DebugInformation e)
        {
            cts.Token.ThrowIfCancellationRequested();

            if (!IsAttached)
            {
                return StepMode.None;
            }

            bool breakPointShouldBreak = HandleBreakpoint(e);

            switch (e.PauseType)
            {
                case PauseType.DebuggerStatement:
                    state = DebuggerState.Stepping;
                    return OnPause(PauseReason.DebuggerStatement, e);

                case PauseType.Break:
                    if (breakPointShouldBreak)
                    {
                        state = DebuggerState.Stepping;
                        return OnPause(PauseReason.Breakpoint, e);
                    }
                    break;
            }

            // Break is only called when we're not stepping - so since we didn't pause, keep running:
            return StepMode.None;
        }

        private bool HandleBreakpoint(DebugInformation info)
        {
            if (info.BreakPoint == null)
            {
                return false;
            }
            if (info.BreakPoint is ExtendedBreakPoint breakpoint)
            {
                // If breakpoint has a hit condition, evaluate it
                if (breakpoint.HitCondition != null)
                {
                    breakpoint.HitCount++;
                    if (!breakpoint.HitCondition(breakpoint.HitCount))
                    {
                        // Don't break if the hit condition wasn't met
                        return false;
                    }
                }

                // If this is a logpoint rather than a breakpoint, log message and don't break
                if (breakpoint.LogMessage != null)
                {
                    var message = Evaluate(breakpoint.LogMessage);
                    LogPoint?.Invoke(message.AsString(), info);
                    return false;
                }
            }

            // Allow breakpoint to break
            return true;
        }

        private StepMode OnPause(PauseReason reason, DebugInformation e)
        {
            CurrentDebugInformation = e;
            Stopped?.Invoke(reason, e);

            PauseThread();
            
            Continued?.Invoke();

            return nextStep;
        }

        private void PauseThread()
        {
            // Pause the thread until waitForContinue is set
            waitForContinue.WaitOne();
            waitForContinue.Reset();
        }
    }
}
