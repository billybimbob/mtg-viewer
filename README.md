# mtg-viewer

A Magic: The Gathering Card Manager and Deck Builder, built using Blazor, Razor Pages, EF Core, all bundled in [ASP.NET](https://dotnet.microsoft.com/en-us/apps/aspnet).

## Features

MTG Viewer is a collaborative collection manager, that enables users to build and share Magic: The Gathering decks. This application is intended for a small group of friends to organize and theorize deck ideas from their personal MTG collection.

* Card collection Management
  * Keep track of cards counts
  * Add, search, and remove individual cards
  * Track card changes
  * Overall collection statistics
  * Import/Backup collection
* User accounts
  * Create user-owned decks
  * Player trading
  * Suggest cards to other users
* Deck building
  * Theorycrafting
  * Sample mulligans
  * Track change history
  * Share a deck preview with everyone

## Web Design Highlights

* Simple, responsive user interface
* Optimized and efficient backend

## Design Philosophy

MTG Viewer was made for the purpose of accessing a physical card collection through the web. This application does this by synchronizing a physical card collection with a virtual one.

This application expects the users to manually add their collections into the application via manually searching or a json/csv formatted file. Note that only cards with multiverse IDs will be processed due to limitations of [Gatherer](https://gatherer.wizards.com/Pages/Default.aspx).

In addition to simply adding the cards to the database, we implemented an organization structure that is seemingly redundant for a virtual collection. This organization structure is a two-tiered system of "bins" and "boxes". In this system, the physical cards would be

This application was built with trusted friends in mind who would not intentionally harm the system. There is however, some safety mechanisms in place in case the user accidentally attempts to modify the collection. Users that register into the application will have the verification emails sent and managed to a specified system administrator. This is done because all verified users have access to the collection and will be able to modify card counts.

The main goal of this application is to keep the user's physical card collection in sync with the virtual one as closely as possible. For that reason, the concept of "theorycrafted" and "built" decks, as well as "wanted" and "held" cards are introduced. These concepts are used in the application to distinguish what changes are being done virtually versus what changes are actually being done to the physical collection.

Since synchronicity between the virtual and physical collections is imperative for this application, multiple systems were implemented to maintain a reliable and resilient data storage. These systems include exporting and importing a database backup upon user request and additional confirmation steps upon committing collection and deck changes.

Once changes are officially committed in the application, the users are expected to reflect these changes to their physical collection.

TODO
