namespace Opossum.Configuration;

public sealed class OpossumOptionsSetup : IConfigureOptions<OpossumOptions>, IValidateOptions<OpossumOptions>
{
    public void Configure(OpossumOptions options)
    {
        throw new NotImplementedException();
    }

    public ValidateOptionsResult Validate(string? name, OpossumOptions options)
    {
        throw new NotImplementedException();
    }
}
