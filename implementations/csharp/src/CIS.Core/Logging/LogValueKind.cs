namespace CIS.Logging;

/// <summary>The explicitly selected portable representation of an attribute value.</summary>
public enum LogValueKind
{
    /// <summary>An explicit null value.</summary>
    Null = 0,
    /// <summary>A Boolean value.</summary>
    Boolean = 1,
    /// <summary>A Unicode string.</summary>
    String = 2,
    /// <summary>A finite floating-point JSON number.</summary>
    Number = 3,
    /// <summary>An integer within the inclusive portable safe range.</summary>
    Integer = 4,
    /// <summary>An explicitly selected canonical integer string.</summary>
    IntegerString = 5,
    /// <summary>An explicitly selected invariant decimal string.</summary>
    DecimalString = 6,
    /// <summary>An explicitly selected UUID string; normalization follows in WP04-S01-T02.</summary>
    Uuid = 7,
    /// <summary>An explicitly selected UTC timestamp string; normalization follows in WP04-S01-T02.</summary>
    Timestamp = 8,
    /// <summary>An ordered array of supported values.</summary>
    Array = 9,
    /// <summary>A map with ordinal, case-sensitive string keys.</summary>
    Map = 10
}
