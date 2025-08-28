---
title: Creating your First Plugin
description: Let's learn to make our first plugin together!
header: https://files.facepunch.com/Alistair/103/07/2025/9944/vlcsnap-2025-07-03-08h13m45s177-min.png
date: 2025-07-05T21:45:36.805Z
tags:
    - tutorial
    - first plugin
    - developer
    - carbon
layout: news-layout
sidebar: false
fmContentType: blogpost
category: beginners-guide
published: true
author: Bubbafett
hidden: false
logo: /news/first-plugin.webp
collectionid: 1
---

<NewsSectionTitle text="Introduction" author="bubbafett5611"/>
<NewsSection marginless>

So, you decided you want to make your own plugin. Welcome! We can help you get started, and build a foundation that will get your feet under you, for now at least.

In this tutorial, we will provide resources as well as instructions on how to setup a development environment, create a simple plugin, and how to load that plugin on a server.

:::warning
This tutorial assumes you know the following:
- How to read
:::

</NewsSection>

<NewsSectionSubtitle text="Let's get started!"/>
<NewsSection marginless>

Alright, let's get some things out of the way first. You need to setup a development environment, no matter if you are making an Oxide or Carbon plugin you can follow the instructions on [this page](https://carbonmod.gg/devs/creating-your-project) since is compatible with Oxide. You can also checkout this [Oxide Doc](https://docs.oxidemod.com/guides/developers/development-environment) for Oxide specific information.

:::info TIP!
Carbon is compatible with Oxide plugins, so I HIGHLY recommend developing on a Carbon test server.
:::

Once you have your development environment setup, we need to look at creating our first plugin. Both pages linked above have example plugins you can copy, but we are going to create ours together.

To kick things off, lets talk namespaces. Think of a namespace like the scope your plugin operates inside of, so depending on which namespace you use, you will have different methods and options you can use to create your plugin.

When creating a plugin, you can use either `Carbon.Plugins` or `Oxide.Plugins`. If you want your plugin to be usable on both Carbon and Oxide servers, you should pick `Oxide.Plugins` but if you are creating a plugin for Carbon you should pick `Carbon.Plugins`. For this tutorial, I am going to use `Carbon.Plugins`.

```cs:line-numbers
namespace Carbon.Plugins;
```

Once your namespace is created, you need to create your class for your plugin. It is standard practice to match the class name to the plugin name. In my case, I am going to use `MyFirstPlugin` for both my plugin name and my class name. 

Let's go ahead and create my class now, and because I am creating a Carbon plugin I am going to inherit from the class `CarbonPlugin` which is provided by Carbon.
```cs:line-numbers
namespace Carbon.Plugins;

public class MyFirstPlugin : CarbonPlugin// [!code ++]
{// [!code ++]
}// [!code ++]
```

Both frameworks also require you to add information about your plugin before your class. This data is used to tell the framework what the title, author, version, and description of the plugin is.
```cs:line-numbers
namespace Carbon.Plugins;

[Info("MyFirstPlugin", "Bubbafett", "1.0.0")]// [!code ++]
[Description("A simple plugin that prints a message when loaded.")]// [!code ++]
public class MyFirstPlugin : CarbonPlugin
{
}
```
At this point, you have the minimum required to be considered a plugin by both frameworks. This, however, is useless in it's current state. Let's fix that.
</NewsSection>
<NewsSectionSubtitle text="Do a flip!"/>
<NewsSection marginless>

So at this point, we have namespace created for our plugin, the metadata, and the class all setup but the class is empty. We need to make it do something, so I am going to make our plugin send a message when the plugin loads.

We can start by adding a hook to the plugin that is called when the plugin loads. Think of hooks as "listeners", they run the code inside the hook when they hear what they are listening for. You can see the entire list of hooks [here](https://carbonmod.gg/references/hooks/).

I'm going to go ahead and create the hook now, but leave it empty.
```cs:line-numbers
namespace Carbon.Plugins;

[Info("MyFirstPlugin", "Bubbafett", "1.0.0")]
[Description("A simple plugin that prints a message when loaded.")]
public class MyFirstPlugin : CarbonPlugin
{
    private void Loaded()// [!code ++]
    {// [!code ++]
    }// [!code ++]
}
```
So, now if we want something to happen when the plugin is loaded we can add it inside that method we just created. With that in mind, we can get into the really hard part of this tutorial, making it actually **do** something.

For the hard part, we are going to use the `Puts()` to print a string into the console. I am also going to create something called a field at the top of the class to store the string we want to print. So, lets create a private field called `_message` and set it to the string `Do a flip!`.
```cs:line-numbers
namespace Carbon.Plugins;

[Info("MyFirstPlugin", "Bubbafett", "1.0.0")]
[Description("A Simple plugin that prints a message when loaded.")]
public class MyFirstPlugin : CarbonPlugin
{
    private string _message = "Do a flip!";// [!code ++]

    private void Loaded()
    {
        Puts(_message);// [!code ++]
    }
}
```

Now, when the plugin loads it will print our message! Isn't that cool?! Well we aren't done yet, so buckle up.
</NewsSection>
<NewsSectionSubtitle text="Rock'n'Roll"/>
<NewsSection>

So we have it printing a message when the plugin loads, but what about when a player **DOES** something? How about we give the player an item when they wake up from a nap?

So, I'm going to listen to the hook `OnPlayerSleepEnded()` which is called after the player has woken up and to test I am just going to print a message to console to make sure it fires like it should when we wake up.
```cs:line-numbers
namespace Carbon.Plugins;

[Info("MyFirstPlugin", "Bubbafett", "1.0.0")]
[Description("A Simple plugin that prints a message when loaded.")]
public class MyFirstPlugin : CarbonPlugin
{
    private string _message = "Do a flip!";
    
    private void Loaded()
    {
        Puts(_message);
    }
    
    private void OnPlayerSleepEnded(BasePlayer basePlayer)// [!code ++]
    {// [!code ++]
        Puts("OnPlayerSleepEnded has been called!");// [!code ++]
    }// [!code ++]
}
```

I'm going to use the `ItemManager` class's `CreateByName()` method to create a rock item. Then, I'll use `Player.GiveItem()` to actually give the item to the player.

So let's remove the `Puts()` inside of `OnPlayerSleepEnded()` and add our item and give it to the player.

Let's also send the player a message telling them we gave them an extra rock using the `ChatMessage()` method from the `BasePlayer` class. If you prefix a string with `$` you can insert dynamic information in the string like item names if you wrap it in `{}`, for example `$"Item Name: {rock.info.displayName.english}"`

I also recommend exploring what options you can use for things like items and player. How you do this will depend on what IDE you have or if you are using a program like ILSpy to look at the existing code!

```cs:line-numbers
namespace Carbon.Plugins;

[Info("MyFirstPlugin", "Bubbafett", "1.0.0")]
[Description("A Simple plugin that prints a message when loaded.")]
public class MyFirstPlugin : CarbonPlugin
{
    private string _message = "Do a flip!";
    
    private void Loaded()
    {
        Puts(_message);
    }
    
    private void OnPlayerSleepEnded(BasePlayer basePlayer)
    {
        Item rock = ItemManager.CreateByName("rock");// [!code ++]
        Player.GiveItem(basePlayer, rock);// [!code ++]
        basePlayer.ChatMessage($"You have been given a {rock.info.displayName.english} for waking up!");// [!code ++]
    }
}
```

And there you have it! An entire functional plugin that does a thing or two!

:::tip Useful Information!

- When listening to a hook, if you don't plan on changing how the game functions you should return `void` instead of `object`
- For hooks like `OnEntitySpawned()` you can replace things like `BaseEntity` with `BasePlayer` or something similar if you don't need to listen to **every** entity spawning

:::
</NewsSection>