using System;
using UnityEngine;

public sealed partial class TeleportAnywhere
{
    static readonly Color Paper = new Color(.84f,.82f,.78f), Light = new Color(.92f,.90f,.86f);
    static readonly Color Ink = new Color(.28f,.25f,.24f), Muted = new Color(.46f,.42f,.40f), Rose = new Color(.56f,.35f,.41f);
    GUIStyle titleStyle, textStyle, smallStyle, buttonStyle, inputStyle, rowStyle;
    Font font;
    Texture2D normalTexture, hoverTexture, activeTexture;
    string category = "全部";
    Texture2D Solid(Color color)
    {
        Texture2D t = new Texture2D(1,1); t.SetPixel(0,0,color); t.Apply(); t.hideFlags = HideFlags.HideAndDontSave; return t;
    }
    void EnsureTheme()
    {
        if(textStyle != null)return;
        font = Font.CreateDynamicFontFromOSFont(new string[] { "Microsoft YaHei", "Noto Sans CJK SC", "SimHei", "Arial" }, 17);
        normalTexture = Solid(Light); hoverTexture = Solid(new Color(.98f,.95f,.90f)); activeTexture = Solid(new Color(.74f,.68f,.65f));
        textStyle = new GUIStyle(GUI.skin.label) { font = font, fontSize = 17, wordWrap = true, richText = false };
        textStyle.normal.textColor = Ink;
        smallStyle = new GUIStyle(textStyle) { fontSize = 14 }; smallStyle.normal.textColor = Muted;
        titleStyle = new GUIStyle(textStyle) { fontSize = 28, fontStyle = FontStyle.Bold };
        buttonStyle = new GUIStyle(GUI.skin.button) { font = font, fontSize = 16, border = new RectOffset(), padding = new RectOffset(10,10,7,7), richText = false };
        buttonStyle.normal.background = normalTexture; buttonStyle.normal.textColor = Ink;
        buttonStyle.hover.background = hoverTexture; buttonStyle.hover.textColor = Ink;
        buttonStyle.active.background = activeTexture; buttonStyle.active.textColor = Ink;
        buttonStyle.focused.background = hoverTexture; buttonStyle.focused.textColor = Ink;
        rowStyle = new GUIStyle(buttonStyle) { alignment = TextAnchor.MiddleLeft, fontSize = 17, wordWrap = true, padding = new RectOffset(14,14,6,6) };
        inputStyle = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 16, padding = new RectOffset(9,9,6,6), richText = false };
        inputStyle.normal.background = normalTexture; inputStyle.normal.textColor = Ink;
        inputStyle.focused.background = hoverTexture; inputStyle.focused.textColor = Ink;
    }
    static void Fill(Rect r, Color c) { Color old = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = old; }
    static void Line(float x, float y, float width, Color color) { Fill(new Rect(x,y,width,1),color); }
    static void Border(Rect r, Color c)
    { Line(r.x,r.y,r.width,c); Line(r.x,r.yMax-1,r.width,c); Fill(new Rect(r.x,r.y,1,r.height),c); Fill(new Rect(r.xMax-1,r.y,1,r.height),c); }
    static void Diamond(float x, float y, float size, Color color)
    {
        Matrix4x4 old = GUI.matrix; GUIUtility.RotateAroundPivot(45,new Vector2(x,y));
        Fill(new Rect(x-size/2,y-size/2,size,size),color); GUI.matrix = old;
    }
    bool Button(Rect r, string text)
    { bool clicked = GUI.Button(r,text,buttonStyle); Border(r,GUI.enabled ? Muted : new Color(.65f,.62f,.58f)); return clicked; }
    void OnGUI()
    {
        EnsureTheme();
        int depth = GUI.depth; Matrix4x4 matrix = GUI.matrix; GUI.depth = -105;
        Color color = GUI.color; GUI.color = Color.white;
        float scale = Mathf.Min(Screen.width / 1020f, Screen.height / 750f, 1.6f);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale,scale,1));
        float width = Screen.width / scale, height = Screen.height / scale;
        if(show)
        {
            Fill(new Rect(0,0,width,height),new Color(.13f,.11f,.10f,.75f));
            Rect panel = new Rect((width-930)/2,(height-650)/2,930,650);
            GUI.BeginGroup(panel);
            DrawPanel();
            GUI.EndGroup();
            if(Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.ScrollWheel)Event.current.Use();
        }
        else
        {
            if(extraPanel.Value && launcher.Value && World != null && World.curMap != null && World.PlayerNoel != null)
            {
                Rect entry = new Rect(22,22,184,38);
                if(Button(entry,"◆  跨地图传送    " + hotkey.Value))SetVisible(true);
            }
            if(travelling || Time.realtimeSinceStartup < noticeUntil)
            {
                Rect toast = new Rect((width-640)/2,height-85,640,59); Fill(toast,Paper); Border(toast,Muted);
                GUI.Label(new Rect(toast.x+16,toast.y+10,608,43),travelling ? "正在传送 · 原版确认框或剧情出现时，请正常操作。" : notice,textStyle);
            }
        }
        GUI.matrix = matrix; GUI.depth = depth; GUI.color = color;
    }
    void DrawPanel()
    {
        Fill(new Rect(0,0,930,650),Paper); Border(new Rect(5,5,920,640),Muted); Border(new Rect(10,10,910,630),Muted);
        GUI.Label(new Rect(34,25,790,40),"Teleport Anywhere",titleStyle);
        GUI.Label(new Rect(35,67,780,27),"跨越区域，回到走过的地方。",smallStyle);
        if(Button(new Rect(840,29,57,35),"关闭"))SetVisible(false);
        Line(34,104,390,Muted); Diamond(465,104,8,Rose); Line(506,104,390,Muted);
        GUI.Label(new Rect(35,120,850,30),"当前位置  /  " + World.WM.CurWM.localized_name + " · " + MapName(World.curMap),textStyle);
        search = GUI.TextField(new Rect(35,164,490,35),search,80,inputStyle);
        if(string.IsNullOrEmpty(search))GUI.Label(new Rect(46,170,450,25),"搜索地区、地图或记录名称…",smallStyle);
        string[] categories = { "全部", "长椅", "记录" };
        for(int i=0;i<categories.Length;i++)
        {
            string name = categories[i];
            if(Button(new Rect(35+i*127,213,117,32),(category == name ? "◆ " : "")+name)) { category = name; scroll = Vector2.zero; }
        }
        if(Button(new Rect(428,213,97,32),"刷新列表"))RefreshDestinations();
        DrawList();
        Fill(new Rect(548,165,1,402),new Color(.65f,.61f,.57f));
        GUI.Label(new Rect(574,164,315,30),"传送目的地",textStyle);
        if(selected != null)
        {
            GUI.Label(new Rect(574,208,315,66),selected.Name,new GUIStyle(titleStyle) {fontSize=22});
            GUI.Label(new Rect(574,280,315,80),selected.Area + "\n" + selected.Kind, textStyle);
            GUI.Label(new Rect(574,359,315,48),selected.MapKey + "\n坐标 " + selected.X + " / " + selected.Y,smallStyle);
            if(Button(new Rect(574,429,315,43),"传送到这里  →"))BeginTravel(selected);
            bool removable = bookmarks.Contains(selected);
            GUI.enabled = removable;
            if(Button(new Rect(574,485,150,34),"删除此记录")) { bookmarks.Remove(selected); selected = null; RefreshDestinations(); }
            GUI.enabled = true;
            if(returnPoint != null && Button(new Rect(736,485,153,34),"返回出发点"))BeginTravel(returnPoint);
        }
        else GUI.Label(new Rect(574,217,315,150),"还没有可用的目的地。\n\n探索并发现长椅，或在下方记录当前位置。",textStyle);
        bookmarkName = GUI.TextField(new Rect(35,527,333,36),bookmarkName,36,inputStyle);
        if(string.IsNullOrEmpty(bookmarkName))GUI.Label(new Rect(46,533,315,25),"记录名称（可留空）",smallStyle);
        if(Button(new Rect(380,527,145,36),"记录当前位置"))AddBookmark();
        Line(35,582,860,Muted);
        string bottom = Time.realtimeSinceStartup < noticeUntil ? notice : "仅显示已发现长椅。记录和返回点只保留本次游玩，读档后清空。";
        GUI.Label(new Rect(35,592,860,25),bottom,smallStyle);
        GUI.Label(new Rect(35,618,860,23),hotkey.Value + " / ESC 关闭  ·  面板内暂停世界并拦截操作  ·  跨区域可能结算危险度与任务",smallStyle);
    }
    void DrawList()
    {
        var visible = destinations.FindAll(delegate(Destination d) {
            if(category == "长椅" && d.Kind != "已发现长椅")return false;
            if(category == "记录" && d.Kind == "已发现长椅")return false;
            return (d.Name + " " + d.Area + " " + d.MapKey).IndexOf(search,StringComparison.CurrentCultureIgnoreCase) >= 0;
        });
        Rect viewport = new Rect(35,259,490,250);
        scroll = GUI.BeginScrollView(viewport,scroll,new Rect(0,0,467,Mathf.Max(249,visible.Count*64)));
        for(int i=0;i<visible.Count;i++)
        {
            Destination d = visible[i]; Rect r = new Rect(0,i*64,466,58);
            bool focused = d == selected;
            if(GUI.Button(r,GUIContent.none,rowStyle))selected = d;
            Border(r,focused ? Rose : new Color(.67f,.63f,.59f));
            if(focused)Fill(new Rect(0,r.y,4,r.height),Rose);
            GUI.Label(new Rect(14,r.y+4,437,27),d.Name,textStyle);
            GUI.Label(new Rect(14,r.y+31,437,23),d.Area + "  ·  " + d.Kind,smallStyle);
        }
        if(visible.Count == 0)GUI.Label(new Rect(14,22,430,80),"没有符合条件的传送点。",textStyle);
        GUI.EndScrollView();
    }
    void ReleaseTheme()
    {
        if(font != null)Destroy(font);
        foreach(Texture2D t in new Texture2D[]{normalTexture,hoverTexture,activeTexture})if(t != null)Destroy(t);
    }
}
