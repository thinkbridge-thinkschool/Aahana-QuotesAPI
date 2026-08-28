import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { describe, expect, it } from 'vitest';

import { QuotesStore } from './quotes.store';
import { QuoteService } from '../services/quote.service';
import { Quote } from '../models/quote';

function makeQuote(id: number): Quote {
  return {
    id,
    userId: 1,
    author: `Author ${id}`,
    text: `Quote ${id}`,
    isDeleted: false
  };
}

describe('QuotesStore', () => {
  function setup() {
    const getQuotesCalls: Subject<Quote[]>[] = [];

    const fakeQuoteService = {
      getQuotes: () => {
        const subject = new Subject<Quote[]>();
        getQuotesCalls.push(subject);
        return subject.asObservable();
      }
    };

    TestBed.configureTestingModule({
      providers: [
        QuotesStore,
        { provide: QuoteService, useValue: fakeQuoteService }
      ]
    });

    const store = TestBed.inject(QuotesStore);

    return { store, getQuotesCalls };
  }

  it('starts empty, not loading, no error', () => {
    const { store } = setup();

    expect(store.quotes()).toEqual([]);
    expect(store.loading()).toBe(false);
    expect(store.error()).toBe(false);
    expect(store.isEmpty()).toBe(true);
  });

  it('sets loading true while the request is in flight, false after it resolves', () => {
    const { store, getQuotesCalls } = setup();

    store.load(1, 10);
    expect(store.loading()).toBe(true);

    getQuotesCalls[0].next([makeQuote(1)]);
    getQuotesCalls[0].complete();

    expect(store.loading()).toBe(false);
    expect(store.quotes()).toEqual([makeQuote(1)]);
  });

  it('sets error true and stops loading on a failed request', () => {
    const { store, getQuotesCalls } = setup();

    store.load(1, 10);
    getQuotesCalls[0].error(new Error('network failure'));

    expect(store.loading()).toBe(false);
    expect(store.error()).toBe(true);
    expect(store.quotes()).toEqual([]);
  });

  it('reports isEmpty when a successful request returns zero quotes', () => {
    const { store, getQuotesCalls } = setup();

    store.load(1, 10);
    getQuotesCalls[0].next([]);
    getQuotesCalls[0].complete();

    expect(store.isEmpty()).toBe(true);
  });

  it('does not let a slow, stale request overwrite a newer one (concurrent updates)', () => {
    const { store, getQuotesCalls } = setup();

    // User navigates to page 1, then quickly to page 2 before page 1's
    // response arrives (e.g. fast clicking, or a slow network on the
    // first request only).
    store.load(1, 10);
    store.load(2, 10);

    expect(getQuotesCalls.length).toBe(2);

    // Page 2's response arrives first (it's a smaller/faster query).
    getQuotesCalls[1].next([makeQuote(20), makeQuote(21)]);
    getQuotesCalls[1].complete();

    // Page 1's response arrives late, after page 2 already resolved.
    getQuotesCalls[0].next([makeQuote(1), makeQuote(2)]);
    getQuotesCalls[0].complete();

    // The store must reflect the LAST request the caller made (page 2),
    // not whichever HTTP response happened to land last.
    expect(store.page()).toBe(2);
    expect(store.quotes()).toEqual([makeQuote(20), makeQuote(21)]);
  });
});
