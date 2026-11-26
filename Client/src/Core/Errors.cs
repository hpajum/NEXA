using System;
using System.IO;

namespace Nexa
{
    internal static class Errors
    {
        /// <summary>Failures the UI reports to the user instead of crashing.</summary>
        public static bool IsExpected(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException ||
                ex is System.Security.Cryptography.CryptographicException ||
                ex is System.Runtime.Serialization.SerializationException ||
                ex is System.Net.Http.HttpRequestException || ex is OperationCanceledException ||
                ex is System.ComponentModel.Win32Exception || ex is ArgumentException || ex is NotSupportedException;
        }
    }
}
