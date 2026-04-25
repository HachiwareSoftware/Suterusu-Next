using Xunit;
using Suterusu.Models;

namespace Suterusu.Tests
{
    public class ResultTypeTests
    {
        // -----------------------------------------------------------------------
        // AiResponseResult
        // -----------------------------------------------------------------------

        [Fact]
        public void AiResponseResult_Ok_HasSuccessTrue()
        {
            var result = AiResponseResult.Ok("Some content", "gpt-5.4-mini");
            Assert.True(result.Success);
        }

        [Fact]
        public void AiResponseResult_Ok_HasCorrectContent()
        {
            var result = AiResponseResult.Ok("Response body", "gpt-5.4-mini");
            Assert.Equal("Response body", result.Content);
        }

        [Fact]
        public void AiResponseResult_Ok_HasCorrectModelUsed()
        {
            var result = AiResponseResult.Ok("Response body", "gpt-5.4-mini");
            Assert.Equal("gpt-5.4-mini", result.ModelUsed);
        }

        [Fact]
        public void AiResponseResult_Ok_ErrorIsNull()
        {
            var result = AiResponseResult.Ok("content", "model");
            Assert.Null(result.Error);
        }

        [Fact]
        public void AiResponseResult_Fail_HasSuccessFalse()
        {
            var result = AiResponseResult.Fail("Something went wrong");
            Assert.False(result.Success);
        }

        [Fact]
        public void AiResponseResult_Fail_ContentIsNull()
        {
            var result = AiResponseResult.Fail("error");
            Assert.Null(result.Content);
        }

        [Fact]
        public void AiResponseResult_Fail_ModelUsedIsNull()
        {
            var result = AiResponseResult.Fail("error");
            Assert.Null(result.ModelUsed);
        }

        [Fact]
        public void AiResponseResult_Fail_HasNonNullError()
        {
            var result = AiResponseResult.Fail("Something went wrong");
            Assert.NotNull(result.Error);
            Assert.Equal("Something went wrong", result.Error);
        }

        // -----------------------------------------------------------------------
        // ClipboardReadResult
        // -----------------------------------------------------------------------

        [Fact]
        public void ClipboardReadResult_Ok_HasSuccessTrue()
        {
            var result = ClipboardReadResult.Ok("clipboard text");
            Assert.True(result.Success);
        }

        [Fact]
        public void ClipboardReadResult_Ok_HasCorrectText()
        {
            var result = ClipboardReadResult.Ok("clipboard text");
            Assert.Equal("clipboard text", result.Text);
        }

        [Fact]
        public void ClipboardReadResult_Ok_ErrorIsNull()
        {
            var result = ClipboardReadResult.Ok("clipboard text");
            Assert.Null(result.Error);
        }

        [Fact]
        public void ClipboardReadResult_Fail_HasSuccessFalse()
        {
            var result = ClipboardReadResult.Fail("read error");
            Assert.False(result.Success);
        }

        [Fact]
        public void ClipboardReadResult_Fail_TextIsNull()
        {
            var result = ClipboardReadResult.Fail("read error");
            Assert.Null(result.Text);
        }

        [Fact]
        public void ClipboardReadResult_Fail_HasNonNullError()
        {
            var result = ClipboardReadResult.Fail("read error");
            Assert.NotNull(result.Error);
        }

        // -----------------------------------------------------------------------
        // ClipboardWriteResult
        // -----------------------------------------------------------------------

        [Fact]
        public void ClipboardWriteResult_Ok_HasSuccessTrue()
        {
            var result = ClipboardWriteResult.Ok();
            Assert.True(result.Success);
        }

        [Fact]
        public void ClipboardWriteResult_Ok_ErrorIsNull()
        {
            var result = ClipboardWriteResult.Ok();
            Assert.Null(result.Error);
        }

        [Fact]
        public void ClipboardWriteResult_Fail_HasSuccessFalse()
        {
            var result = ClipboardWriteResult.Fail("write error");
            Assert.False(result.Success);
        }

        [Fact]
        public void ClipboardWriteResult_Fail_HasNonNullError()
        {
            var result = ClipboardWriteResult.Fail("write error");
            Assert.NotNull(result.Error);
            Assert.Equal("write error", result.Error);
        }

        // -----------------------------------------------------------------------
        // HotkeyActionResult
        // -----------------------------------------------------------------------

        [Fact]
        public void HotkeyActionResult_Ok_HasSuccessTrue()
        {
            var result = HotkeyActionResult.Ok();
            Assert.True(result.Success);
        }

        [Fact]
        public void HotkeyActionResult_Ok_ErrorIsNull()
        {
            var result = HotkeyActionResult.Ok();
            Assert.Null(result.Error);
        }

        [Fact]
        public void HotkeyActionResult_Fail_HasSuccessFalse()
        {
            var result = HotkeyActionResult.Fail("hotkey error");
            Assert.False(result.Success);
        }

        [Fact]
        public void HotkeyActionResult_Fail_HasNonNullError()
        {
            var result = HotkeyActionResult.Fail("hotkey error");
            Assert.NotNull(result.Error);
            Assert.Equal("hotkey error", result.Error);
        }

        // -----------------------------------------------------------------------
        // SaveConfigResult
        // -----------------------------------------------------------------------

        [Fact]
        public void SaveConfigResult_Ok_HasSuccessTrue()
        {
            var result = SaveConfigResult.Ok();
            Assert.True(result.Success);
        }

        [Fact]
        public void SaveConfigResult_Ok_ErrorIsNull()
        {
            var result = SaveConfigResult.Ok();
            Assert.Null(result.Error);
        }

        [Fact]
        public void SaveConfigResult_Fail_HasSuccessFalse()
        {
            var result = SaveConfigResult.Fail("save error");
            Assert.False(result.Success);
        }

        [Fact]
        public void SaveConfigResult_Fail_HasNonNullError()
        {
            var result = SaveConfigResult.Fail("save error");
            Assert.NotNull(result.Error);
            Assert.Equal("save error", result.Error);
        }

        // -----------------------------------------------------------------------
        // AiSingleAttemptResult
        // -----------------------------------------------------------------------

        [Fact]
        public void AiSingleAttemptResult_Ok_HasSuccessTrue()
        {
            var result = AiSingleAttemptResult.Ok("response content");
            Assert.True(result.Success);
        }

        [Fact]
        public void AiSingleAttemptResult_Ok_HasCorrectContent()
        {
            var result = AiSingleAttemptResult.Ok("response content");
            Assert.Equal("response content", result.Content);
        }

        [Fact]
        public void AiSingleAttemptResult_Ok_ErrorIsNull()
        {
            var result = AiSingleAttemptResult.Ok("response content");
            Assert.Null(result.Error);
        }

        [Fact]
        public void AiSingleAttemptResult_Fail_HasSuccessFalse()
        {
            var result = AiSingleAttemptResult.Fail("attempt error");
            Assert.False(result.Success);
        }

        [Fact]
        public void AiSingleAttemptResult_Fail_ContentIsNull()
        {
            var result = AiSingleAttemptResult.Fail("attempt error");
            Assert.Null(result.Content);
        }

        [Fact]
        public void AiSingleAttemptResult_Fail_HasNonNullError()
        {
            var result = AiSingleAttemptResult.Fail("attempt error");
            Assert.NotNull(result.Error);
            Assert.Equal("attempt error", result.Error);
        }

        // -----------------------------------------------------------------------
        // CliProxyResult
        // -----------------------------------------------------------------------

        [Fact]
        public void CliProxyResult_Ok_HasSuccessTrue()
        {
            var result = CliProxyResult.Ok("ready");
            Assert.True(result.Success);
            Assert.Equal("ready", result.Message);
            Assert.Null(result.Error);
        }

        [Fact]
        public void CliProxyResult_Fail_HasError()
        {
            var result = CliProxyResult.Fail("failed");
            Assert.False(result.Success);
            Assert.Equal("failed", result.Error);
            Assert.Null(result.Message);
        }

        // -----------------------------------------------------------------------
        // CliProxyHealthResult
        // -----------------------------------------------------------------------

        [Fact]
        public void CliProxyHealthResult_Ok_ExposesModelList()
        {
            var result = CliProxyHealthResult.Ok(new[] { "gpt-5.3-codex" });
            Assert.True(result.Success);
            Assert.Single(result.Models);
            Assert.Null(result.Error);
        }

        [Fact]
        public void CliProxyHealthResult_Fail_HasErrorAndEmptyModels()
        {
            var result = CliProxyHealthResult.Fail("unreachable");
            Assert.False(result.Success);
            Assert.Equal("unreachable", result.Error);
            Assert.Empty(result.Models);
        }
    }
}
