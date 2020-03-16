using System;

namespace sim6502
{
    public class PluralFormatProvider : IFormatProvider, ICustomFormatter
    {
        public object GetFormat(Type formatType) {
            return this;
        }
        
        public string Format(string format, object arg, IFormatProvider formatProvider) {
            var forms = format.Split(';');
            var value = (int)arg;
            var form = value == 1 ? 0 : 1;
            return value + " " + forms[form];
        }
    }
}