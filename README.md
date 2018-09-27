# GMWebExtension

An experimental GameMaker: Studio extension to run a Chromium web browser inside a game using CEF (or CefSharp, more precisely). Was meant to do things like YouTube embeds, so there is no support for input yet.

The extension currently supports Windows only as I have no idea if it's even possible to make an extension that interacts with GM:S surfaces so all I can do is pass the pointer to DirectX device.

Note that it currently does not work if you try to run the app directly from Studio as it tries to look for DLLs in the Runner directory. You have to make a build for it to work.

The interface:
```gml
browser_id = browser_create(initial_url)
browser_destroy(browser_id)
browser_load(browser_id, url)
browser_load_html(browser_id, html)
browser_resize(browser_id, width, height)
browser_draw(browser_id, x, y)
browser_is_initialized(browser_id)
browser_js(browser_id, js)

// In the Social async event (this is what all extensions are supposed to use apparently and I hate it):
var event_id = async_load[? "id"];

if (event_id == browser_event) {
    var type = async_load[? "type"];
    var browser_id = async_load[? "browser"];
    if (type == "browser_initialized") {
        show_message("Browser initialized");
    }
    if (type == "frame_load_end") {
        var is_main_frame = async_load[? "is_main_frame"];
        var url = async_load[? "url"];
        if (is_main_frame) {
            show_message("Main frame loaded!");
        }
    }
    if (type == "loading_state_changed") {
        var loading = async_load[? "loading"];
        if (loading) {
            show_debug_message("Loading started");
        } else {
            show_debug_message("Loading finished");
        }
    }
}
```
