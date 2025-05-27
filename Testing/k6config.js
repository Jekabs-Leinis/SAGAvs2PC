import http from 'k6/http';

export const options = {
  scenarios: {
    constant_request_rate_1: {
      executor: 'constant-arrival-rate',
      rate: 20, 
      timeUnit: '1s',
      duration: '10m', 
      preAllocatedVUs: 100,
      maxVUs: 10000,
      exec: 'scenario1',
    },
    constant_request_rate_2: {
      executor: 'constant-arrival-rate',
      startTime: '10m',
      rate: 40,
      timeUnit: '1s',
      duration: '10m',
      preAllocatedVUs: 100,
      maxVUs: 10000,
      exec: 'scenario2',
    },
    constant_request_rate_3: {
      executor: 'constant-arrival-rate',
      startTime: '20m',
      rate: 60,
      timeUnit: '1s',
      duration: '10m',
      preAllocatedVUs: 100,
      maxVUs: 10000,
      exec: 'scenario3',
    },
    constant_request_rate_4: {
      executor: 'constant-arrival-rate',
      startTime: '30m',
      rate: 80,
      timeUnit: '1s',
      duration: '10m',
      preAllocatedVUs: 100,
      maxVUs: 10000,
      exec: 'scenario4',
    },
    constant_request_rate_5: {
      executor: 'constant-arrival-rate',
      startTime: '40m',
      rate: 100,
      timeUnit: '1s',
      duration: '10m',
      preAllocatedVUs: 100,
      maxVUs: 10000,
      exec: 'scenario5',
    },
  },
};

function makeRequest(productOffset) {
  const payload = {
    "userId": Math.floor(Math.random()*1000),
    "productId": productOffset + Math.floor(Math.random()*1000),
    "quantity": Math.floor(Math.random()*10),
    "amount": Math.round(Math.random()*1000) / 100
  }

  const params = {
    headers: {
      'Content-Type': 'application/json',
    },
  };
  
  var res = http.post('http://SAGA-ALB-31550931.eu-north-1.elb.amazonaws.com:8080/transaction/saga/order', JSON.stringify(payload), params);

  try {
    var json = res.json(); 
    console.log(json.runtime + ',' +  json.statusCode + ',' + (json.failedAt || ""));
  } catch (e) {
    console.log('Response was not JSON:', res);
    console.error('Error making request:', e);
  }

}

export function scenario1() { makeRequest(0); }
export function scenario2() { makeRequest(1000); }
export function scenario3() { makeRequest(2000); }
export function scenario4() { makeRequest(3000); }
export function scenario5() { makeRequest(4000); }

