using System;
using System.Runtime.Serialization;

namespace Crypton.TerraformStateService
{
    [Serializable]
    internal class StateLockedException : Exception
    {

        public string LockData
        {
            get;
            private set;
        }

        public StateLockedException(string message, string lockData) : base(message)
        {
            LockData = lockData;
        }

    }
}