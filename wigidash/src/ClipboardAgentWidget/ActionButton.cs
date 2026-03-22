using System;
using System.Drawing;

namespace ClipboardAgentWidget
{
    public enum ActionType
    {
        // LLM actions (require server)
        Summarize,
        Refactor,
        Explain,
        FixBug,
        // Local actions (no server needed)
        Format,
        Transform,
        Snippet,
        Escape
    }

    public enum ButtonState
    {
        Ready,
        Processing,
        Success,
        Error
    }

    public class ActionButton
    {
        public ActionType Action { get; set; }
        public string DisplayName { get; set; }
        public Rectangle Bounds { get; set; }
        public ButtonState State { get; set; }
        public string SystemPrompt { get; set; }
        public Color ActiveColor { get; set; }
        public Color InactiveColor { get; set; }

        public bool IsLLMAction
        {
            get
            {
                return Action == ActionType.Summarize || Action == ActionType.Refactor ||
                       Action == ActionType.Explain || Action == ActionType.FixBug;
            }
        }

        public ActionButton()
        {
            State = ButtonState.Ready;
            ActiveColor = Color.FromArgb(0, 120, 200);
            InactiveColor = Color.FromArgb(64, 64, 64);
        }

        // === LLM Action Buttons ===

        public static ActionButton CreateSummarize(Rectangle bounds)
        {
            return new ActionButton
            {
                Action = ActionType.Summarize,
                DisplayName = "Summarize",
                Bounds = bounds,
                SystemPrompt = "You are a helpful assistant. Summarize the following text concisely, capturing the key points in 2-3 sentences.",
                ActiveColor = Color.FromArgb(0, 150, 136)
            };
        }

        public static ActionButton CreateRefactor(Rectangle bounds)
        {
            return new ActionButton
            {
                Action = ActionType.Refactor,
                DisplayName = "Refactor",
                Bounds = bounds,
                SystemPrompt = "You are an expert programmer. Refactor the following code to improve readability, maintainability, and performance. Keep the same functionality. Return only the refactored code.",
                ActiveColor = Color.FromArgb(156, 39, 176)
            };
        }

        public static ActionButton CreateExplain(Rectangle bounds)
        {
            return new ActionButton
            {
                Action = ActionType.Explain,
                DisplayName = "Explain",
                Bounds = bounds,
                SystemPrompt = "You are a helpful teacher. Explain the following code or text in simple terms. Break down complex concepts and provide examples where helpful.",
                ActiveColor = Color.FromArgb(33, 150, 243)
            };
        }

        public static ActionButton CreateFixBug(Rectangle bounds)
        {
            return new ActionButton
            {
                Action = ActionType.FixBug,
                DisplayName = "Fix Bug",
                Bounds = bounds,
                SystemPrompt = "You are an expert debugger. Analyze the following code, identify any bugs or issues, and provide the corrected code. Explain what was wrong briefly.",
                ActiveColor = Color.FromArgb(244, 67, 54)
            };
        }

        // === Local Action Buttons ===

        public static ActionButton CreateFormat(Rectangle bounds)
        {
            return new ActionButton
            {
                Action = ActionType.Format,
                DisplayName = "Format",
                Bounds = bounds,
                ActiveColor = Color.FromArgb(46, 125, 50) // Green
            };
        }

        public static ActionButton CreateTransform(Rectangle bounds)
        {
            return new ActionButton
            {
                Action = ActionType.Transform,
                DisplayName = "Transform",
                Bounds = bounds,
                ActiveColor = Color.FromArgb(230, 126, 34) // Orange
            };
        }

        public static ActionButton CreateSnippet(Rectangle bounds)
        {
            return new ActionButton
            {
                Action = ActionType.Snippet,
                DisplayName = "Snippet",
                Bounds = bounds,
                ActiveColor = Color.FromArgb(0, 172, 193) // Cyan
            };
        }

        public static ActionButton CreateEscape(Rectangle bounds)
        {
            return new ActionButton
            {
                Action = ActionType.Escape,
                DisplayName = "Escape",
                Bounds = bounds,
                ActiveColor = Color.FromArgb(255, 160, 0) // Amber
            };
        }
    }
}
