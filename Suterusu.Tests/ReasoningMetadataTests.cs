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

        [Fact]
        public void ExtractReasoningEfforts_DoesNotInventLevelsFromSupportedParameters()
        {
            var metadata = JObject.Parse(@"{
                ""id"": ""openrouter-model"",
                ""supported_parameters"": [""temperature"", ""reasoning"", ""include_reasoning""]
            }");

            var efforts = ModelPriorityEditor.ExtractReasoningEfforts(metadata).ToList();

            Assert.Empty(efforts);
        }

        [Fact]
        public void ExtractReasoningEfforts_ReadsNestedReasoningEffortFields()
        {
            var metadata = JObject.Parse(@"{
                ""reasoning"": { ""efforts"": [""minimal"", ""high""] },
                ""capabilities"": { ""reasoning"": { ""levels"": [""xhigh""] } }
            }");

            var efforts = ModelPriorityEditor.ExtractReasoningEfforts(metadata).ToList();

            Assert.Equal(new[] { "minimal", "high", "xhigh" }, efforts);
        }

        [Fact]
        public void ExtractReasoningEffortsFromDetails_ReadsEndpointReasoningLevels()
        {
            var details = JObject.Parse(@"{
                ""data"": {
                    ""endpoints"": [
                        { ""reasoning"": { ""levels"": [""low"", ""high""] } },
                        { ""capabilities"": { ""reasoning"": { ""efforts"": [""xhigh""] } } }
                    ]
                }
            }");

            var efforts = ModelPriorityEditor.ExtractReasoningEffortsFromDetails(details).ToList();

            Assert.Equal(new[] { "low", "high", "xhigh" }, efforts);
        }
    }
}
