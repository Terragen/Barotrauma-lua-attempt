local defaultLib = {}

local CreateStatic = LuaSetup.CreateStatic
local CreateEnum = LuaUserData.CreateEnumTable

local localizedStrings = {
    "LocalizedString", "LimitLString", "WrappedLString", "AddedPunctuationLString", "CapitalizeLString", "ConcatLString", "FallbackLString", "FormattedLString", "InputTypeLString", "JoinLString", "LowerLString", "RawLString", "ReplaceLString", "ServerMsgLString", "SplitLString", "TagLString", "TrimLString", "UpperLString", "StripRichTagsLString",
}

for key, value in pairs(localizedStrings) do
	defaultLib[value] = CreateStatic("Barotrauma." .. value, true)
end

defaultLib["Sprite"] = CreateStatic("Barotrauma.Sprite", true)
defaultLib["PlayerInput"] = CreateStatic("Barotrauma.PlayerInput", true)

defaultLib["Keys"] = CreateStatic("Microsoft.Xna.Framework.Input.Keys", true)

defaultLib["GUI"] = {
    GUI = CreateStatic("Barotrauma.GUI", true),
    GUIStyle = CreateStatic("Barotrauma.GUIStyle", true),
    RectTransform = CreateStatic("Barotrauma.RectTransform", true),
    LayoutGroup = CreateStatic("Barotrauma.GUILayoutGroup", true),
    Button = CreateStatic("Barotrauma.GUIButton", true),
    TextBox = CreateStatic("Barotrauma.GUITextBox", true),
    Canvas = CreateStatic("Barotrauma.GUICanvas", true),
    Frame = CreateStatic("Barotrauma.GUIFrame", true),
    TextBlock = CreateStatic("Barotrauma.GUITextBlock", true),
    TickBox = CreateStatic("Barotrauma.GUITickBox", true),
    Image = CreateStatic("Barotrauma.GUIImage", true),
    ListBox = CreateStatic("Barotrauma.GUIListBox", true),
    ScrollBar = CreateStatic("Barotrauma.GUIScrollBar", true),
    DropDown = CreateStatic("Barotrauma.GUIDropDown", true),
    NumberInput = CreateStatic("Barotrauma.GUINumberInput", true),

    Screen = CreateStatic("Barotrauma.Screen"),

    Anchor = CreateStatic("Barotrauma.Anchor"),
    Alignment = CreateStatic("Barotrauma.Alignment"),
    Pivot = CreateStatic("Barotrauma.Pivot"),
}

return defaultLib