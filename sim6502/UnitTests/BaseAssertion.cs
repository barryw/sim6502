using NLog;

namespace sim6502.UnitTests
{
    public abstract class BaseAssertion
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
        
        public abstract bool PerformAssertion(Processor proc, ExpressionParser expr, TestUnitTest test, TestAssertion assertion);
        
        /// <summary>
        /// Write out a message to the log
        /// </summary>
        /// <param name="level">The log level to use for the message</param>
        /// <param name="message">The message to write</param>
        protected void WriteMessage(LogLevel level, string message)
        {
            Logger.Log(level, message);
        }

        /// <summary>
        /// Write a failure message to the log
        /// </summary>
        /// <param name="message"></param>
        /// <param name="test"></param>
        /// <param name="assertion"></param>
        protected void WriteFailureMessage(string message, TestUnitTest test, TestAssertion assertion)
        {
            WriteMessage(LogLevel.Fatal, $"{message} for '{assertion.Description}' assertion of test '{test.Name}'");
        }
    }
}