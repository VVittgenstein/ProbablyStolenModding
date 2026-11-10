using System;
using System.Collections.Generic;
using ES3Internal;

namespace F5ForceSave
{
    // Use the game's ES3 parser, including its nonstandard dictionary/quote
    // syntax, but stop once the menu's existing fields have been read.
    internal static class SaveHeader
    {
        internal static bool TryRead(ES3Reader reader, out SaveRecordFields fields)
        {
            fields = null;
            var json = reader as ES3JSONReader;
            if (json == null || !FindObject(json, "playerStore") || !FindObject(json, "value")) return false;
            var found = new SaveRecordFields();
            bool data = false, state = false, outing = false;
            string name;
            while ((name = json.ReadPropertyName()) != null)
            {
                switch (name)
                {
                    case "modData": found.modData = json.Read<Dictionary<string, string>>(); data = true; break;
                    case "storeState": found.storeState = json.Read<int>(); state = true; break;
                    case "lastAfterhourOutingDay": found.lastAfterhourOutingDay = json.Read<int>(); outing = true; break;
                    default: json.Skip(); break;
                }
                if (data && state && (outing || found.storeState != 0)) { fields = found; return true; }
            }
            return false;
        }

        private static bool FindObject(ES3JSONReader json, string target)
        {
            string name;
            while ((name = json.ReadPropertyName()) != null)
            {
                if (name == target)
                {
                    int value;
                    do { value = json.baseReader.Read(); } while (value >= 0 && char.IsWhiteSpace((char)value));
                    return value == '{';
                }
                json.Skip();
            }
            return false;
        }
    }
}
