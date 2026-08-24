using System;

namespace Nexo
{
    public enum OwnFieldType
    {
        Text,
        Date
    }

    /// <summary>
    /// Pole własne do ustawienia na dokumencie - nazwa pola + wartość wraz z jej typem.
    /// </summary>
    public class OwnField
    {
        public string Name { get; set; }
        public OwnFieldType Type { get; set; }
        public object Value { get; set; }

        public static OwnField Text(string name, string value)
        {
            return new OwnField { Name = name, Type = OwnFieldType.Text, Value = value };
        }

        public static OwnField Date(string name, DateTime? value)
        {
            return new OwnField { Name = name, Type = OwnFieldType.Date, Value = value };
        }

        /// <summary>
        /// Pole pomijamy, gdy nie skonfigurowano jego nazwy albo nie podano wartości.
        /// </summary>
        public bool IsEmpty
        {
            get
            {
                if (string.IsNullOrEmpty(Name) || Value == null)
                {
                    return true;
                }

                return Value is string text && string.IsNullOrEmpty(text);
            }
        }
    }
}
