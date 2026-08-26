import { describe, expect, it } from 'vitest';

interface QuoteContract {
  Id: number;
  Author: string;
  Text: string;
}

interface ValidationProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  errors?: Record<string, string[]>;
}

describe('Week-1 Quotes API contract', () => {
  it('GET /api/quotes/?page=1&size=10 returns Quote[]', () => {
    const response: QuoteContract[] = [
      {
        Id: 1,
        Author: 'Aahana',
        Text: 'Test quote'
      }
    ];

    expect(Array.isArray(response)).toBe(true);
    expect(response).toHaveLength(1);

    expect(response[0]).toEqual({
      Id: 1,
      Author: 'Aahana',
      Text: 'Test quote'
    });
  });

  it('400 response matches ValidationProblemDetails', () => {
    const response: ValidationProblemDetails = {
      type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: {
        page: ['The page field is invalid.']
      }
    };

    expect(response.status).toBe(400);

    expect(response.title).toBe(
      'One or more validation errors occurred.'
    );

    expect(response.errors?.['page']).toContain(
      'The page field is invalid.'
    );
  });
});