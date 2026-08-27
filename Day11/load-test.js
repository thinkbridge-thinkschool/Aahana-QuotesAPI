import http from 'k6/http';
import { Trend } from 'k6/metrics';

const responseTime = new Trend('slow_endpoint_duration', true);

export const options = {
    vus: 10,
    duration: '10s',

    summaryTrendStats: [
        'avg',
        'min',
        'med',
        'max',
        'p(90)',
        'p(95)',
        'p(99)',
    ],
};

export default function () {
    const response = http.get('http://localhost:5050/slow');

    responseTime.add(response.timings.duration);
}