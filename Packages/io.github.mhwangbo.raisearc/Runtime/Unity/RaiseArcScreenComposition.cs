using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaiseArc.Unity
{
    public enum ScreenPartKind { Panel, Character, Stats, Activities, Timetable, Text, Button }
    public enum ScreenButtonAction { None, SaveDraft, LoadDraft, RemoveSlot, CopyDay, ConfirmPlan, ResumePlan, NewPlan }

    public enum CardField { Image, Name, Description, Cost, Duration }

    [Serializable]
    public sealed class ScreenPart
    {
        public string id = "", title = "", titleKo = "", category = "";
        public ScreenPartKind kind;
        public ScreenButtonAction action;
        public Rect bounds = new Rect(5, 5, 30, 30);
        public Color background = new Color(.12f, .17f, .22f, .95f);
        public Color foreground = new Color(.94f, .91f, .83f);
        public int fontSize = 16, padding = 12;
        public bool horizontalCards, horizontalFields;
        public int cardWidth = 200, cardHeight = 88, itemPadding = 10, imageSize = 40, slotHeight = 36;
        public List<CardField> cardFields = new List<CardField> { CardField.Image, CardField.Name, CardField.Description, CardField.Cost, CardField.Duration };
        public Color itemBackground = new Color(.18f,.25f,.29f), itemText = Color.white;
        public Color selectedBackground = new Color(.83f,.72f,.48f), selectedText = new Color(.08f,.10f,.12f);
        public Color placedBackground = new Color(.23f,.35f,.36f), lockedBackground = new Color(.17f,.22f,.26f);
        public Color runningBackground = new Color(.24f,.47f,.38f), problemBackground = new Color(.54f,.19f,.19f);
    }

    public static class ScreenComposition
    {
        // Coordinates are percentages of the canvas; ordering in this list is the paint order.
        public static List<ScreenPart> WeeklyStarter() => new List<ScreenPart>
        {
            Part("heading", ScreenPartKind.Text, "A week of possibilities", "가능성이 자라는 일주일", 3, 3, 59, 10, 27),
            Part("character", ScreenPartKind.Character, "Elara", "엘라라", 3, 15, 23, 48),
            Part("stats", ScreenPartKind.Stats, "Today", "오늘의 상태", 3, 65, 23, 31),
            Part("activities", ScreenPartKind.Activities, "Choose an activity", "활동 선택", 28, 15, 24, 69),
            Part("week", ScreenPartKind.Timetable, "Your weekly plan", "나의 주간 계획", 54, 15, 43, 69),
            Part("remove", ScreenPartKind.Button, "Clear slot", "선택 칸 비우기", 54, 87, 13, 9, action: ScreenButtonAction.RemoveSlot),
            Part("copy", ScreenPartKind.Button, "Copy to next day", "다음 날로 복사", 68, 87, 13, 9, action: ScreenButtonAction.CopyDay),
            Part("confirm", ScreenPartKind.Button, "Confirm plan", "계획 확정", 82, 87, 15, 9, action: ScreenButtonAction.ConfirmPlan),
            Part("save", ScreenPartKind.Button, "Save", "저장", 66, 3, 15, 9, action: ScreenButtonAction.SaveDraft),
            Part("load", ScreenPartKind.Button, "Load", "불러오기", 82, 3, 15, 9, action: ScreenButtonAction.LoadDraft)
        };

        public static ScreenPart Part(string id, ScreenPartKind kind, string title, string ko,
            float x, float y, float w, float h, int font = 16, ScreenButtonAction action = ScreenButtonAction.None)
            => new ScreenPart { id = id, kind = kind, title = title, titleKo = ko, bounds = new Rect(x, y, w, h), fontSize = font, action = action };

        public static void Validate(IReadOnlyList<ScreenPart> parts)
        {
            if (parts == null || parts.Count > 64) throw new ArgumentException("Screen requires at most 64 parts.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in parts)
            {
                if (p == null || string.IsNullOrWhiteSpace(p.id) || !ids.Add(p.id)) throw new ArgumentException("Screen part IDs must be unique.");
                var r = p.bounds;
                if (!Finite(r.x) || !Finite(r.y) || !Finite(r.width) || !Finite(r.height) || r.x < 0 || r.y < 0 || r.width < 2 || r.height < 2 || r.xMax > 100 || r.yMax > 100)
                    throw new ArgumentException("Keep screen parts inside the canvas (0–100%).");
                if (!Enum.IsDefined(typeof(ScreenPartKind), p.kind) || !Enum.IsDefined(typeof(ScreenButtonAction), p.action) || p.fontSize < 10 || p.fontSize > 48 || p.padding < 0 || p.padding > 40)
                    throw new ArgumentException("Invalid screen part style or action.");
                if (p.cardFields == null || p.cardFields.Count > 5 || new HashSet<CardField>(p.cardFields).Count != p.cardFields.Count || p.cardFields.Exists(v => !Enum.IsDefined(typeof(CardField), v)) || p.cardWidth < 100 || p.cardWidth > 800 || p.cardHeight < 40 || p.cardHeight > 500 || p.slotHeight < 24 || p.slotHeight > 120 || p.itemPadding < 0 || p.itemPadding > 32 || p.imageSize < 16 || p.imageSize > 200)
                    throw new ArgumentException("Invalid repeated card or timetable cell style.");
            }
        }
        private static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
    }
}
