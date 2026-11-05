using MxControls.Agent;
using Xunit;

namespace MxControls.Tests;

public class AgentBuildTests
{
    // The app's hash_file test uses the same vector; the two must agree or every launch restarts the agent.
    [Fact]
    public void HashFile_is_lowercase_hex_sha256()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "abc");
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", AgentBuild.HashFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
