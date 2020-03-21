/*
Copyright (c) 2020 Barry Walker. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met: 

1. Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer. 
2. Redistributions in binary form must reproduce the above copyright notice,
this list of conditions and the following disclaimer in the documentation
and/or other materials provided with the distribution. 

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

using NLog;

namespace sim6502.UnitTests
{
    public abstract class BaseAssertion
    {
        protected static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        public abstract bool PerformAssertion(Processor proc, ExpressionParser expr, TestUnitTest test,
            TestAssertion assertion);

        /// <summary>
        /// Write out a message to the log
        /// </summary>
        /// <param name="level">The log level to use for the message</param>
        /// <param name="message">The message to write</param>
        private static void WriteMessage(LogLevel level, string message)
        {
            Logger.Log(level, message);
        }

        /// <summary>
        /// Write a failure message to the log and indicate the assertion and the name of the test
        /// </summary>
        /// <param name="message">The message to display</param>
        /// <param name="test">The test that's currently running</param>
        /// <param name="assertion">The assertion within the test that's running</param>
        protected static void WriteFailureMessage(string message, TestUnitTest test, TestAssertion assertion)
        {
            WriteMessage(LogLevel.Fatal, $"{message} for '{assertion.Description}' assertion of test '{test.Name}'");
        }
    }
}