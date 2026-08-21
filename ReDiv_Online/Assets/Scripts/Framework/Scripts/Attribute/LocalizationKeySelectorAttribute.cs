using System;

[AttributeUsage(AttributeTargets.Field)]
public sealed class LocalizationKeySelectorAttribute : Attribute
{
    public readonly string TableFieldName;

    public LocalizationKeySelectorAttribute(string tableFieldName)
    {
        TableFieldName = tableFieldName;
    }
}