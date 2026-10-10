namespace CIS.Configuration;

internal interface IEnvironmentVariableSource
{
    string? GetValue(string name);
}
