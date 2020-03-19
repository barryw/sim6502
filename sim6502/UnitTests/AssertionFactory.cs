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

using NLog;

namespace sim6502.UnitTests
{
    public class AssertionFactory
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Return the correct assertion implementation
        /// </summary>
        /// <param name="assertion">The values for the assertion</param>
        /// <returns>The correct assertion implementation based on the values on the assertion</returns>
        public static BaseAssertion GetAssertionClass(TestAssertion assertion)
        {
            if (!assertion.Address.Empty() && assertion.ByteCount.Empty())
            {
                Logger.Trace($"Using MemoryTestAssertion for {assertion.Description}");
                return new MemoryTestAssertion();
            }

            if (!assertion.Address.Empty() && !assertion.ByteCount.Empty())
            {
                Logger.Trace($"Using MemoryBlockAssertion for {assertion.Description}");
                return new MemoryBlockAssertion();
            }

            if (!assertion.Register.Empty())
            {
                Logger.Trace($"Using ProcessorRegisterAssertion for {assertion.Description}");
                return new ProcessorRegisterAssertion();
            }

            if (!assertion.CycleCount.Empty())
            {
                Logger.Trace($"Using CycleCountAssertion for {assertion.Description}");
                return new CycleCountAssertion();
            }

            return null;
        }
    }
}