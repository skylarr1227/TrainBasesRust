---
title: Localization
description: Learn to create customizable, multi-language, messages for your plugins.
header: https://files.facepunch.com/Alistair/128/04/2025/3P09/jungleupdate_jungleruins_22.jpg
date: 2025-07-05T13:42:51.509Z
tags:
    - carbon
    - developer
    - locale
    - oxide
    - tutorial
    - lang
layout: news-layout
sidebar: false
fmContentType: blogpost
category: beginners-guide
published: true
author: Bubbafett
hidden: false
logo: /news/localization.webp
collectionid: 3
---

<NewsSectionTitle text="Introduction" author="bubbafett5611"/>
<NewsSection marginless>

When you release plugins to the public, it is a good idea to have the plugin support localization. Luckily, supporting message customization and multiple languages is extremely easy to do!

In this tutorial, we will cover how to create lang files for your plugin and how to support localization in your plugin.

:::warning
This tutorial assumes you know the following:
- How to create basic Carbon or Oxide plugins
- Basic C# syntax and structure (classes, methods, fields)
- How to run and test your plugin on a Rust server
:::

</NewsSection>

<NewsSectionSubtitle text="What are Lang Files?"/>
<NewsSection marginless>

Lang files are JSON files created by both Carbon and Oxide to allow plugins to support multiple languages in their messages to users. This allows for greater customization and accessability.

At their core, each lang JSON is just a dictionary of strings that allow you to lookup a specific message key and return in the correct language for the user it is sending the message to using their Rust Client language settings.

Lang also supports string replacement, allowing you to send users dynamic messages.

**Example:**
```json
{
  "cooldown_player": "You're cooled down. Please wait {0}.",
  "unknown_chat_cmd_1": "<color=orange>Unknown command:</color> {0}",
  "unknown_chat_cmd_2": "<color=orange>Unknown command:</color> {0}\n<size=12s>Suggesting: {1}</size>",
  "unknown_chat_cmd_separator_1": ", ",
  "unknown_chat_cmd_separator_2": " or ",
  "no_perm": "You don't have any of the required permissions to run this command.",
  "no_group": "You aren't in any of the required groups to run this command.",
  "no_auth": "You don't have the minimum auth level [{0}] required to execute this command [your level: {1}]."
}
```

</NewsSection>
<NewsSectionSubtitle text="Creating Lang Files!"/>
<NewsSection marginless>

When a plugin loads, it calls the method `LoadDefaultMessages()` from the `Plugin` class. So, to create our first lang files we have to override this method with our own. Since lang relies on dictionaries, we need to make sure we are using `System.Collections.Generic` to make them available to us.

In order to actually register the messages with the framework we need to use `lang.RegisterMessages()` which will take our dictionary and create a lang file for the selected language from it. So in my example, I'm going to create a simple `Hello` message that will greet a player when they use a chat command. I'm also going to make sure I select the language I am registering, in this case I am using `"en"`.

```cs:line-numbers
using System.Collections.Generic;

    protected override void LoadDefaultMessages()
    {
        lang.RegisterMessages(new Dictionary<string, string>
        {
            ["Hello"] = "Hello there!",
        }, this, "en");
    }
```

Now, already we can use this message without much effort! I generally create a helper method for sending localized chat messages, so lets go through that now.

So first, I'll create a new method called `LangMessage()` that takes a `BasePlayer` and a `string` for the message key.

Inside that method, I'm using to use a method from BasePlayer called `ChatMessage()` to send the message to the player's chat.

```cs:line-numbers
using System.Collections.Generic;

    protected override void LoadDefaultMessages()
    {
        lang.RegisterMessages(new Dictionary<string, string>
        {
            ["Hello"] = "Hello there!",
        }, this, "en");
    }

    private void LangMessage(BasePlayer player, string key)// [!code ++]
    {// [!code ++]
        player.ChatMessage(lang.GetMessage(key, this, player.UserIDString));// [!code ++]
    }// [!code ++]
```

So now, we have our messages being created, and a helper method to let us easily send those messages to a player's chat. Now we just need to create a command that will actually send the message.

I'll do this by create a new method called `CmdHello()` and putting our helper method we just created inside of it. When creating the method, we need to make sure our method signature is `BasePlayer player, string command, string[] args` for the command to function correctly. Depending on the namespace you are using, you might use `IPlayer` instead of `BasePlayer`.

```cs:line-numbers
using System.Collections.Generic;

    protected override void LoadDefaultMessages()
    {
        lang.RegisterMessages(new Dictionary<string, string>
        {
            ["Hello"] = "Hello there!",
        }, this, "en");
    }
    
    private void LangMessage(BasePlayer player, string key)
    {
        player.ChatMessage(lang.GetMessage(key, this, player.UserIDString));
    }
    
    private void CmdHello(BasePlayer player, string command, string[] args)// [!code ++]
    {// [!code ++]
        LangMessage(player, "Hello");// [!code ++]
    }// [!code ++]
```

We also need to register the command with the framework, there are a few ways to do this but today I'm doing to register the command manually when the plugin initializes using the `Init()` hook.

I'm also going to add a permission check to make sure the user can use the command, so I'll need to register the permission as well. So, I'm going to create a field that will store the string we use as the permission `HelloThere.use`.

I'll also go ahead and add a message to my dictionary of message that I will use to tell players they don't have permission.

```cs:line-numbers
using System.Collections.Generic;

    public const string PermUse = "HelloThere.use";// [!code ++]
    
    protected override void LoadDefaultMessages()
    {
        lang.RegisterMessages(new Dictionary<string, string>
        {
            ["Hello"] = "Hello there!",
            ["NoPerm"] = "You do not have permission to use this command.",// [!code ++]
        }, this, "en");
    }
    
    private void Init()// [!code ++]
    {// [!code ++]
        cmd.AddChatCommand("hi", this, nameof(CmdHello));// [!code ++]
        permission.RegisterPermission(PermUse, this);// [!code ++]
    }// [!code ++]
    
    private void LangMessage(BasePlayer player, string key)
    {
        player.ChatMessage(lang.GetMessage(key, this, player.UserIDString));
    }
    
    private void CmdHello(BasePlayer player, string command, string[] args)
    {
        if(!permission.UserHasPermission(player.UserIDString, PermUse))// [!code ++]
        {// [!code ++]
            LangMessage(player, "NoPerm");// [!code ++]
            return;// [!code ++]
        }// [!code ++]
        
        LangMessage(player, "Hello");
    }
```

While this is great, you can't really build the messages dynamically with the current system. So, I'm going to add an overload for lang message that accepts a string array called `args`. I'm also going to use `string.Format` to allow us to parse the lang message and replace `{0}` with a players name. Then I'll send a second message to the player greeting them by their name.

```cs:line-numbers
using System.Collections.Generic;

    public const string PermUse = "HelloThere.use";
    
    protected override void LoadDefaultMessages()
    {
        lang.RegisterMessages(new Dictionary<string, string>
        {
            ["Hello"] = "Hello there!",
            ["NoPerm"] = "You do not have permission to use this command.",
            ["Welcome"] = "Welcome to the server, {0}!", // [!code ++]
        }, this, "en");
    }
    
    private void Init()
    {
        cmd.AddChatCommand("hi", this, nameof(CmdHello));
        permission.RegisterPermission(PermUse, this);
    }
    
    private void LangMessage(BasePlayer player, string key)
    {
        player.ChatMessage(lang.GetMessage(key, this, player.UserIDString));
    }
    
    private void LangMessage(BasePlayer player, string key, params string[] args) // [!code ++]
    {// [!code ++]
        player.ChatMessage(string.Format(lang.GetMessage(key, this, player.UserIDString),args));// [!code ++]
    }// [!code ++]
    
    private void CmdHello(BasePlayer player, string command, string[] args)
    {
        if(!permission.UserHasPermission(player.UserIDString, PermUse))
        {
            LangMessage(player, "NoPerm");
            return;
        }
        
        LangMessage(player, "Hello");
        LangMessage(player,"Welcome", player.displayName);// [!code ++]
    }
```

Congratulations! You have create a fully functional localized plugin, you can register multiple languages in `LoadDefaultMessages()` just make sure you match it to the correctly language code like `es` for spanish!

</NewsSection>
<NewsSectionSubtitle text="Full Example Plugin!"/>
<NewsSection marginless>

You can find my full example plugin below!

```cs
using System.Collections.Generic;

namespace Oxide.Plugins;

[Info("HelloThere", "Bubbafett", "1.0.1")]
[Description("Cool plugin that tells players about cool permissions.")]
public class HelloThere : RustPlugin
{
    public const string PermUse = "HelloThere.use";
    
    protected override void LoadDefaultMessages()
    {
        lang.RegisterMessages(new Dictionary<string, string>
        {
            ["Hello"] = "Hello there!",
            ["NoPerm"] = "You do not have permission to use this command.",
            ["Welcome"] = "Welcome to the server, {0}!",
        }, this, "en");
    }
    
    private void Init()
    {
        cmd.AddChatCommand("hi", this, nameof(CmdHello));
        permission.RegisterPermission(PermUse, this);
    }
    
    private void LangMessage(BasePlayer player, string key)
    {
        player.ChatMessage(lang.GetMessage(key, this, player.UserIDString));
    }
    
    private void LangMessage(BasePlayer player, string key, params string[] args)
    {
        player.ChatMessage(string.Format(lang.GetMessage(key, this, player.UserIDString),args));
    }
    
    private void CmdHello(BasePlayer player, string command, string[] args)
    {
        if(!permission.UserHasPermission(player.UserIDString, PermUse))
        {
            LangMessage(player, "NoPerm");
            return;
        }
        
        LangMessage(player, "Hello");
        LangMessage(player,"Welcome", player.displayName);
    }
}
```
</NewsSection>