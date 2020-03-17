namespace sim6502.UnitTests
{
    public class AssertionFactory
    {
        /// <summary>
        /// Return the correct assertion implementation
        /// </summary>
        /// <param name="assertion">The values for the assertion</param>
        /// <returns>The correct assertion implementation based on the values on the assertion</returns>
        public static BaseAssertion GetAssertionClass(TestAssertion assertion)
        {
            if (!assertion.Address.Empty() && assertion.ByteCount.Empty())
            {
                return new MemoryTestAssertion();
            }

            if (!assertion.Address.Empty() && !assertion.ByteCount.Empty())
            {
                return new MemoryBlockAssertion();
            }
            
            return null;
        }
    }
}