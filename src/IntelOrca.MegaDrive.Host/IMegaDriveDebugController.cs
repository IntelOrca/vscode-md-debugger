using System;
using System.Collections.Immutable;

namespace IntelOrca.MegaDrive.Host
{
    public interface IMegaDriveDebugController
    {
        event EventHandler<string> OnStateChanged;

        ImmutableArray<uint> Breakpoints { get; set; }
        uint PC { get; }
        bool Running { get; }

        void Break();
        void Resume();
        void Step(int n);
        void StepOver();
        void StepOut();

        string Evaluate(string s);
        string SetExpression(string name, string value);
    }
}
