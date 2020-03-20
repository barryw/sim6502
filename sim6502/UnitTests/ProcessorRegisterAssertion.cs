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

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies, 
either expressed or implied, of the FreeBSD Project.
*/
namespace sim6502.UnitTests
{
    public class ProcessorRegisterAssertion : BaseAssertion
    {
        /// <summary>
        /// Compare one of the processor's registers with an expected value
        /// </summary>
        /// <param name="proc">A reference to the running 6502</param>
        /// <param name="expr">A reference to our expression parser</param>
        /// <param name="test">The current test that's running</param>
        /// <param name="assertion">The current assertion within the test that we'd like to test</param>
        /// <returns>True if the assertion passed, False otherwise</returns>
        public override bool PerformAssertion(Processor proc, ExpressionParser expr, TestUnitTest test, TestAssertion assertion)
        {
            var register = assertion.Register.ToLower();
            
            var registerValue = 0;

            switch (register)
            {
                case "a":
                    registerValue = proc.Accumulator;
                    break;
                case "x":
                    registerValue = proc.XRegister;
                    break;
                case "y":
                    registerValue = proc.YRegister;
                    break;
                case "pc":
                    registerValue = proc.ProgramCounter;
                    break;
                case "s":
                    registerValue = proc.StackPointer;
                    break;
                case "n":
                    registerValue = FlagToInt(proc.NegativeFlag);
                    break;
                case "v":
                    registerValue = FlagToInt(proc.OverflowFlag);
                    break;
                case "d":
                    registerValue = FlagToInt(proc.DecimalFlag);
                    break;
                case "i":
                    registerValue = FlagToInt(proc.DisableInterruptFlag);
                    break;
                case "z":
                    registerValue = FlagToInt(proc.ZeroFlag);
                    break;
                case "c":
                    registerValue = FlagToInt(proc.CarryFlag);
                    break;
                default:
                    WriteFailureMessage($"{register} is not a valid register value. Valid values are a, x, y, pc, s, n, v, d, i, z, c", test, assertion);
                    return false;
            }

            var assertValue = assertion.AssertionValue(expr, test);
            var res = assertion.CompareValues(registerValue, assertValue);
            if(!res.ComparisonPassed)
                WriteFailureMessage(res.FailureMessage, test, assertion);

            return res.ComparisonPassed;
        }

        /// <summary>
        /// Convert a flag to an integer value
        /// </summary>
        /// <param name="flag">The flag's value</param>
        /// <returns>1 if the flag is true, 0 otherwise</returns>
        private static int FlagToInt(bool flag)
        {
            return flag ? 1 : 0;
        }
    }
}