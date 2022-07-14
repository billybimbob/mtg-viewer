# Design Insights

## Virtualized Collection

MTG Viewer was made for the purpose of accessing and managing a physical card collection through the web. This is done by synchronizing a physical card collection with a virtual one. The synchronization process between the application and the physical collection is the most important detail of this application.  Once changes are officially committed in the application, the users are expected to reflect these changes to their physical collection.

In addition to simply adding the cards to the database, we implemented a specialized storage structure for the virtual collection. The reason for implementing the structure is to specifically mirror the characteristics of the represented physical collection. This organization structure is a two-tiered system of "bins" and "boxes". In this system, the physical cards are fragmented into various boxes, and then all of the boxes are split across each of the bins.

The application enables for users to search and view many cards through a straightforward, simple interface. Building on top of this feature, we also implemented deck building functionality. Deckbuilding adds the ability to not only read the collection, but to also **modify** it well. Since any modifications to the virtual collection introduces a possible desynchronization point with the physical collection, the concepts of "theorycrafted" and "built" decks were introduced, as well as "wanted" and "held" cards. These concepts are used in the application to distinguish which modifications are pending and which ones have been committed.

## Data Resiliency

Multiple systems were implemented to maintain a reliable and resilient data storage. These systems include exporting and importing a collection/deck backup upon user request and additional confirmation steps upon committing collection and deck changes. We implemented these systems because of the large amounts of data a user has to process to initially use the application. These systems ensure that the user has to do the minimal amount of physical labor to get the application running.

This application expects the users to manually add their collections into the application via manually searching or a json/csv formatted file. Note that only cards with multiverse IDs will be processed due to limitations of [Gatherer](https://gatherer.wizards.com/Pages/Default.aspx).

## Security

This application was built with trusted friends in mind who would not intentionally harm the system. There is however, some safety mechanisms in place in to safeguard against accidental changes to the collection. Users that register into the application will have the verification emails sent and managed to a specified system administrator. This is done because all verified users have access to the collection and will be able to modify card counts.

## App Pages

Below are highlights of how the features are separated across various application web pages.

### Home

Wanting to maintain a welcoming atmosphere, we decided to have the main page be simple and interactive. Some of the elements in this page include an assortment of card images that have been registered into the application. Additionally, we also added a "collection history' near the bottom so that users can quickly view if any committed changes.

### Collection

The collection page shows the users a complete overview of every single card that is registered into the application. Each of the cards are sorted in alphabetical order by default in a table as row entries. Additionally, other common information is displayed such as: Mana cost, Set name, Rarity, and Total Copies.

This page is where users can add individual cards into the application as well as see a general statistical overview of the collection. To make this page slightly more interactive, we also added a card preview when the user hovers over the name of the card.

### Treasury

This section is a view of all the available cards organized into the "bins" and "boxes" structure described [above](#virtualized-collection). This page keeps track of several important factors such as the maximum loading capacity on each box, followed by the names and counts of the cards that are inside the box.

This page is where users can reflect additional physical capacity changes, such as adding more boxes and bins. Finally, the database backup features are accessible in this page for users who wish to download or import a backup.

### Players

This page shows users all the fully registered users as well as the number of decks they have registered in the application. Selecting a user will bring up a page with more details of their created decks.

### Decks

This page shows the user's decks they have created. It also shows details such as the deck's color identity, as well as the current state (theorycraft or built) of the deck. From this page, the user can further view the contents of the deck by clicking on the deck name. They can also edit, delete, or create decks in this page.

### Trades

This page is for users to trade cards they have claimed from the system. The page shows the user's decks as well as managing individual deck requests. The user can choose to send or respond to requests from this page.
