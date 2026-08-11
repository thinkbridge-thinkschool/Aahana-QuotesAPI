# Why a Rich Domain Model?

The original `Quote` entity was anemic: it only exposed public properties and contained no rules about valid state. That meant any part of the application could create or modify a quote without going through a consistent set of domain rules.

The rich model moves those rules into `Quote` itself. `Quote.Create(author, text)` is now the controlled creation path and enforces the author's 1–200 character limit and the text's 1–1000 character limit. The `Text` property cannot be changed after creation because its setter is private. Soft deletion is also represented as domain behavior through `SoftDelete()`, rather than allowing application code to directly manipulate deletion state.

This protects the aggregate regardless of where it is used. The API is no longer responsible for knowing every business rule; it asks the domain to create or modify a quote.

For example, with the old anemic model, a future endpoint could accidentally accept an empty author or a 1500-character quote, or modify the text of an existing quote directly. The compiler and domain rules would not prevent this. With the rich model, invalid creation is rejected by the domain and existing text cannot be changed through the public API of the entity.

The main benefit is that business rules now live with the data they protect, making the model safer, easier to test, and harder to misuse.