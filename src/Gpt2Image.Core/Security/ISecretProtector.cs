namespace Gpt2Image.Core.Security;

public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedText);
}
