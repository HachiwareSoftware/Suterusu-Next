using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Suterusu.Models
{
    public sealed class HotkeyBinding : IEquatable<HotkeyBinding>
    {
        public HotkeyBinding(Keys primaryKey, bool control, bool alt, bool shift, bool windows)
        {
            PrimaryKey = primaryKey;
            Control = control;
            Alt = alt;
            Shift = shift;
            Windows = windows;
        }

        public Keys PrimaryKey { get; }

        public bool Control { get; }

        public bool Alt { get; }

        public bool Shift { get; }

        public bool Windows { get; }

        public string ToDisplayString()
        {
            var parts = new List<string>();

            if (Control)
                parts.Add("Ctrl");
            if (Alt)
                parts.Add("Alt");
            if (Shift)
                parts.Add("Shift");
            if (Windows)
                parts.Add("Win");

            parts.Add(PrimaryKey.ToString().ToUpperInvariant());
            return string.Join("+", parts);
        }

        public bool Equals(HotkeyBinding other)
        {
            if (ReferenceEquals(null, other))
                return false;

            return PrimaryKey == other.PrimaryKey
                && Control == other.Control
                && Alt == other.Alt
                && Shift == other.Shift
                && Windows == other.Windows;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as HotkeyBinding);
        }

        public override int GetHashCode()
        {
            int hashCode = (int)PrimaryKey;
            hashCode = (hashCode * 397) ^ Control.GetHashCode();
            hashCode = (hashCode * 397) ^ Alt.GetHashCode();
            hashCode = (hashCode * 397) ^ Shift.GetHashCode();
            hashCode = (hashCode * 397) ^ Windows.GetHashCode();
            return hashCode;
        }
    }
}
