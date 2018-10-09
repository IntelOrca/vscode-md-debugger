using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelOrca.MegaDrive.Host;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Thread = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.Thread;

namespace IntelOrca.MegaDrive.Debugger
{
    internal class MegaDriveDebugAdapter : DebugAdapterBase
    {
        private MegaDriveHost Host { get; }
        private IMegaDriveDebugController Controller { get; }
        private DebugMap DebugMap { get; set; }
        private CancellationTokenSource HostWindowCTS { get; set; }
        private Task HostTask { get; set; }

        public MegaDriveDebugAdapter(Stream input, Stream output)
        {
            Host = new MegaDriveHost();
            Controller = Host;
            Controller.OnStateChanged += Controller_OnStateChanged;
            InitializeProtocolClient(input, output);
        }

        private void Controller_OnStateChanged(object sender, string reason)
        {
            if (Controller.Running)
            {
                Protocol.SendEvent(new ContinuedEvent());
            }
            else
            {
                var stoppedReason = StoppedEvent.ReasonValue.Unknown;
                switch (reason)
                {
                    case "breakpoint":
                        stoppedReason = StoppedEvent.ReasonValue.Breakpoint;
                        break;
                    case "pause":
                        stoppedReason = StoppedEvent.ReasonValue.Pause;
                        break;
                    case "step":
                        stoppedReason = StoppedEvent.ReasonValue.Step;
                        break;
                }

                Protocol.SendEvent(new StoppedEvent()
                {
                    Reason = stoppedReason,
                    ThreadId = 0,
                    AllThreadsStopped = true
                });
            }
        }

        protected override InitializeResponse HandleInitializeRequest(InitializeArguments arguments)
        {
            return new InitializeResponse()
            {
                SupportsSetVariable = true,
                SupportsEvaluateForHovers = true
            };
        }

        protected override LaunchResponse HandleLaunchRequest(LaunchArguments arguments)
        {
            var romPath = (string)arguments.ConfigurationProperties["rom"];
            var sramPath = (string)arguments.ConfigurationProperties["sram"];
            var mapPath = (string)arguments.ConfigurationProperties["map"];

            Host.LoadGame(romPath);
            var sram = new Sram(Host, sramPath);
            sram.Load();
            DebugMap = new DebugMap(mapPath);
            HostWindowCTS = new CancellationTokenSource();

            HostTask = Task.Run(
                () =>
                {
                    using (var w = new MegaDriveWindow())
                    {
                        w.Run(sram, Host, HostWindowCTS.Token);
                    }
                    Protocol.SendEvent(new TerminatedEvent());
                    Protocol.SendEvent(new ExitedEvent(0));
                });

            Protocol.SendEvent(new InitializedEvent());
            return new LaunchResponse();
        }

        protected override DisconnectResponse HandleDisconnectRequest(DisconnectArguments arguments)
        {
            HostWindowCTS.Cancel();
            HostTask.Wait();
            return new DisconnectResponse();
        }

        protected override PauseResponse HandlePauseRequest(PauseArguments arguments)
        {
            Controller.Break();
            return new PauseResponse();
        }

        protected override ContinueResponse HandleContinueRequest(ContinueArguments arguments)
        {
            Controller.Resume();
            return new ContinueResponse();
        }

        protected override StackTraceResponse HandleStackTraceRequest(StackTraceArguments arguments)
        {
            var stackFrames = new List<StackFrame>();
            var frame = new StackFrame();

            var mapping = DebugMap?.GetMapping(Controller.PC);
            if (mapping.HasValue)
            {
                frame.Line = mapping.Value.Line;
                frame.Name = "main";
                frame.Source = new Source()
                {
                    Path = mapping.Value.Path
                };
            }

            stackFrames.Add(frame);
            return new StackTraceResponse()
            {
                StackFrames = stackFrames,
                TotalFrames = 1
            };
        }

        protected override ResponseBody HandleProtocolRequest(string requestType, object requestArgs)
        {
            return base.HandleProtocolRequest(requestType, requestArgs);
        }

        protected override ThreadsResponse HandleThreadsRequest(ThreadsArguments arguments)
        {
            return new ThreadsResponse()
            {
                Threads = new List<Thread>
                {
                    new Thread(0, "main")
                }
            };
        }

        protected override ScopesResponse HandleScopesRequest(ScopesArguments arguments)
        {
            return new ScopesResponse(new List<Scope>()
            {
                new Scope()
                {
                    Name = "Registers",
                    NamedVariables = 17,
                    VariablesReference = 1
                }
            });
        }

        protected override VariablesResponse HandleVariablesRequest(VariablesArguments arguments)
        {
            if (arguments.VariablesReference == 1)
            {
                var names = new[] {
                    "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7",
                    "A0", "A1", "A2", "A3", "A4", "A5", "A6", "SP",
                    "PC"
                };

                return new VariablesResponse()
                {
                    Variables = names.Select(x => new Variable { Name = x, Value = Controller.Evaluate(x) }).ToList()
                };
            }
            return new VariablesResponse();
        }

        protected override NextResponse HandleNextRequest(NextArguments arguments)
        {
            Controller.StepOver();
            return new NextResponse();
        }

        protected override StepInResponse HandleStepInRequest(StepInArguments arguments)
        {
            Controller.Step(1);
            return new StepInResponse();
        }

        protected override StepOutResponse HandleStepOutRequest(StepOutArguments arguments)
        {
            Controller.StepOut();
            return new StepOutResponse();
        }

        protected override SetBreakpointsResponse HandleSetBreakpointsRequest(SetBreakpointsArguments arguments)
        {
            var breakpoints = new List<Breakpoint>();
            var addresses = new List<uint>();
            foreach (var bp in arguments.Breakpoints)
            {
                bool foundLine = false;
                for (int i = 0; i < 4; i++)
                {
                    var mapping = DebugMap?.GetMapping(arguments.Source.Path, bp.Line + i);
                    if (mapping.HasValue)
                    {
                        foundLine = true;
                        breakpoints.Add(new Breakpoint()
                        {
                            Line = mapping.Value.Line,
                            Verified = true
                        });
                        addresses.Add(mapping.Value.Address);
                        break;
                    }
                }
                if (!foundLine)
                {
                    breakpoints.Add(new Breakpoint()
                    {
                        Line = bp.Line,
                        Verified = false
                    });
                }
            }

            Controller.Breakpoints = addresses.ToImmutableArray();

            return new SetBreakpointsResponse(breakpoints);
        }

        protected override EvaluateResponse HandleEvaluateRequest(EvaluateArguments arguments)
        {
            var expression = arguments.Expression;
            var symbolAddress = DebugMap?.GetSymbolAddress(expression.Trim());
            if (symbolAddress.HasValue)
            {
                expression = $"[{symbolAddress.Value}]";
            }

            var result = Controller.Evaluate(expression);
            if (result != null)
            {
                return new EvaluateResponse()
                {
                    Result = result
                };
            }
            else
            {
                return new EvaluateResponse()
                {
                    Result = "<unknown symbol>",
                    PresentationHint = new VariablePresentationHint()
                    {
                        Attributes = VariablePresentationHint.AttributesValue.FailedEvaluation
                    }
                };
            }
        }

        protected override SetVariableResponse HandleSetVariableRequest(SetVariableArguments arguments)
        {
            var value = Controller.SetExpression(arguments.Name, arguments.Value);
            return new SetVariableResponse()
            {
                Value = value
            };
        }
    }
}
