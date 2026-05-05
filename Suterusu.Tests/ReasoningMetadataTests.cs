using System.Linq;
using Newtonsoft.Json.Linq;
using Suterusu.UI;
using Xunit;

namespace Suterusu.Tests
{
    public class ReasoningMetadataTests
    {
        [Fact]
        public void ExtractReasoningEfforts_ReadsCommonDirectMetadataFields()
        {
            var metadata = JObject.Parse(@"{
                ""reasoning_efforts"": [""none"", ""low""],
                ""supported_reasoning_levels"": [""medium"", ""high""],
                ""reasoning"": { ""levels"": [""xhigh""] },
                ""capabilities"": { ""reasoning_efforts"": [""low"", ""custom-level""] }
            }");

            var efforts = ModelPriorityEditor.ExtractReasoningEfforts(metadata).ToList();

            Assert.Equal(new[] { "none", "low", "medium", "high", "xhigh", "custom-level" }, efforts);
        }

        [Fact]
        public void ExtractReasoningEfforts_DoesNotInferFromModelName()
        {
            var metadata = JObject.Parse(@"{ ""id"": ""gpt-5.5"" }");

            var efforts = ModelPriorityEditor.ExtractReasoningEfforts(metadata);

            Assert.Empty(efforts);
        }
    }
}
