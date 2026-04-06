using System.Collections.Generic;
using Xunit;
using Suterusu.Models;
using Suterusu.Services;

namespace Suterusu.Tests
{
    public class ChatHistoryTests
    {
        // -----------------------------------------------------------------------
        // BuildRequestMessages — baseline structure
        // -----------------------------------------------------------------------

        [Fact]
        public void BuildRequestMessages_NoHistory_ReturnsTwoMessages_SystemAndUser()
        {
            var history = new ChatHistory("Be helpful.", 10);

            var messages = history.BuildRequestMessages("Hello");

            Assert.Equal(2, messages.Count);
            Assert.Equal("system", messages[0].Role);
            Assert.Equal("user",   messages[1].Role);
            Assert.Equal("Hello",  messages[1].Content);
        }

        [Fact]
        public void BuildRequestMessages_NoHistory_UserContentIsCorrect()
        {
            var history = new ChatHistory("Be helpful.", 10);

            var messages = history.BuildRequestMessages("What is 2+2?");

            Assert.Equal("What is 2+2?", messages[messages.Count - 1].Content);
        }

        [Fact]
        public void BuildRequestMessages_WithHistory_IncludesSystemThenTurnsThenUser()
        {
            var history = new ChatHistory("Be helpful.", 10);
            history.AppendSuccessfulTurn("First question", "First answer");

            var messages = history.BuildRequestMessages("Second question");

            // [system, user(first), assistant(first), user(second)]
            Assert.Equal(4, messages.Count);
            Assert.Equal("system",    messages[0].Role);
            Assert.Equal("user",      messages[1].Role);
            Assert.Equal("assistant", messages[2].Role);
            Assert.Equal("user",      messages[3].Role);
        }

        [Fact]
        public void BuildRequestMessages_WithMultipleTurns_OrderIsPreserved()
        {
            var history = new ChatHistory("Sys.", 10);
            history.AppendSuccessfulTurn("Q1", "A1");
            history.AppendSuccessfulTurn("Q2", "A2");

            var messages = history.BuildRequestMessages("Q3");

            // [system, user(Q1), assistant(A1), user(Q2), assistant(A2), user(Q3)]
            Assert.Equal(6, messages.Count);
            Assert.Equal("Q1", messages[1].Content);
            Assert.Equal("A1", messages[2].Content);
            Assert.Equal("Q2", messages[3].Content);
            Assert.Equal("A2", messages[4].Content);
            Assert.Equal("Q3", messages[5].Content);
        }

        // -----------------------------------------------------------------------
        // System message inclusion / exclusion
        // -----------------------------------------------------------------------

        [Fact]
        public void BuildRequestMessages_IncludesSystemMessage_WhenSystemPromptIsNonEmpty()
        {
            var history  = new ChatHistory("You are a bot.", 10);
            var messages = history.BuildRequestMessages("Hi");

            Assert.Equal("system",         messages[0].Role);
            Assert.Equal("You are a bot.", messages[0].Content);
        }

        [Fact]
        public void BuildRequestMessages_ExcludesSystemMessage_WhenSystemPromptIsEmpty()
        {
            var history  = new ChatHistory("", 10);
            var messages = history.BuildRequestMessages("Hi");

            Assert.Single(messages);
            Assert.Equal("user", messages[0].Role);
        }

        [Fact]
        public void BuildRequestMessages_ExcludesSystemMessage_WhenSystemPromptIsNull()
        {
            var history  = new ChatHistory(null, 10);
            var messages = history.BuildRequestMessages("Hi");

            Assert.Single(messages);
            Assert.Equal("user", messages[0].Role);
        }

        // -----------------------------------------------------------------------
        // AppendSuccessfulTurn — storage
        // -----------------------------------------------------------------------

        [Fact]
        public void AppendSuccessfulTurn_StoresUserAndAssistantMessages()
        {
            var history = new ChatHistory("Sys.", 10);
            history.AppendSuccessfulTurn("User text", "Assistant text");

            var msgs = history.Messages;

            Assert.Equal(2, msgs.Count);
            Assert.Equal("user",      msgs[0].Role);
            Assert.Equal("User text", msgs[0].Content);
            Assert.Equal("assistant",      msgs[1].Role);
            Assert.Equal("Assistant text", msgs[1].Content);
        }

        [Fact]
        public void AppendSuccessfulTurn_AccumulatesMultipleTurns()
        {
            var history = new ChatHistory("Sys.", 10);
            history.AppendSuccessfulTurn("Q1", "A1");
            history.AppendSuccessfulTurn("Q2", "A2");

            Assert.Equal(4, history.Messages.Count);
        }

        // -----------------------------------------------------------------------
        // History trimming when limit exceeded
        // -----------------------------------------------------------------------

        [Fact]
        public void AppendSuccessfulTurn_TrimsOldestTurn_WhenLimitExceeded()
        {
            // limit = 1 means only 1 turn (user+assistant pair) stored
            var history = new ChatHistory("Sys.", 1);
            history.AppendSuccessfulTurn("Old Q", "Old A");
            history.AppendSuccessfulTurn("New Q", "New A");

            var msgs = history.Messages;

            Assert.Equal(2, msgs.Count);
            Assert.Equal("New Q", msgs[0].Content);
            Assert.Equal("New A", msgs[1].Content);
        }

        [Fact]
        public void AppendSuccessfulTurn_KeepsExactLimit_WhenAtCapacity()
        {
            var history = new ChatHistory("Sys.", 2);
            history.AppendSuccessfulTurn("Q1", "A1");
            history.AppendSuccessfulTurn("Q2", "A2");

            // Both turns fit exactly within limit=2
            Assert.Equal(4, history.Messages.Count);
        }

        [Fact]
        public void AppendSuccessfulTurn_TrimsToLimit_WhenMultipleExceed()
        {
            var history = new ChatHistory("Sys.", 2);
            history.AppendSuccessfulTurn("Q1", "A1");
            history.AppendSuccessfulTurn("Q2", "A2");
            history.AppendSuccessfulTurn("Q3", "A3");

            // Only the 2 most recent turns (Q2+A2, Q3+A3) should remain
            var msgs = history.Messages;
            Assert.Equal(4, msgs.Count);
            Assert.Equal("Q2", msgs[0].Content);
            Assert.Equal("A2", msgs[1].Content);
            Assert.Equal("Q3", msgs[2].Content);
            Assert.Equal("A3", msgs[3].Content);
        }

        // -----------------------------------------------------------------------
        // Reset
        // -----------------------------------------------------------------------

        [Fact]
        public void Reset_ClearsAllMessages()
        {
            var history = new ChatHistory("Sys.", 10);
            history.AppendSuccessfulTurn("Q", "A");

            history.Reset("New system prompt.");

            Assert.Empty(history.Messages);
        }

        [Fact]
        public void Reset_UpdatesSystemPrompt()
        {
            var history = new ChatHistory("Old prompt.", 10);
            history.Reset("New prompt.");

            Assert.Equal("New prompt.", history.SystemPrompt);
        }

        [Fact]
        public void Reset_NewSystemPromptAppearsInNextBuild()
        {
            var history = new ChatHistory("Old prompt.", 10);
            history.Reset("New prompt.");

            var messages = history.BuildRequestMessages("Hi");

            Assert.Equal("New prompt.", messages[0].Content);
        }

        // -----------------------------------------------------------------------
        // UpdateConfiguration
        // -----------------------------------------------------------------------

        [Fact]
        public void UpdateConfiguration_UpdatesSystemPrompt()
        {
            var history = new ChatHistory("Old.", 10);
            history.UpdateConfiguration("Updated.", 10);

            Assert.Equal("Updated.", history.SystemPrompt);
        }

        [Fact]
        public void UpdateConfiguration_UpdatesHistoryLimit()
        {
            var history = new ChatHistory("Sys.", 10);
            history.UpdateConfiguration("Sys.", 5);

            Assert.Equal(5, history.HistoryLimit);
        }

        [Fact]
        public void UpdateConfiguration_TrimsToNewLimit_WhenLimitReduced()
        {
            var history = new ChatHistory("Sys.", 5);
            history.AppendSuccessfulTurn("Q1", "A1");
            history.AppendSuccessfulTurn("Q2", "A2");
            history.AppendSuccessfulTurn("Q3", "A3");

            // Reduce limit to 1 — only the most recent turn should remain
            history.UpdateConfiguration("Sys.", 1);

            var msgs = history.Messages;
            Assert.Equal(2, msgs.Count);
            Assert.Equal("Q3", msgs[0].Content);
            Assert.Equal("A3", msgs[1].Content);
        }

        [Fact]
        public void UpdateConfiguration_DoesNotTrim_WhenLimitIncreased()
        {
            var history = new ChatHistory("Sys.", 2);
            history.AppendSuccessfulTurn("Q1", "A1");
            history.AppendSuccessfulTurn("Q2", "A2");

            history.UpdateConfiguration("Sys.", 10);

            // All 4 messages (2 turns) should still be present
            Assert.Equal(4, history.Messages.Count);
        }

        // -----------------------------------------------------------------------
        // Messages property reflects stored turns only (no system message)
        // -----------------------------------------------------------------------

        [Fact]
        public void Messages_DoesNotContainSystemMessage()
        {
            var history = new ChatHistory("System prompt here.", 10);
            history.AppendSuccessfulTurn("Q", "A");

            foreach (var msg in history.Messages)
            {
                Assert.NotEqual("system", msg.Role);
            }
        }
    }
}
