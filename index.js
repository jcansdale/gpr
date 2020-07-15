#!/usr/bin/env node

const path = require('path');
const { spawn } = require('child_process');

let platform = process.platform;
let command = require('@jcansdale-test/dotnet-runtime');

if (!command) {
  console.error(`The ${platform} platform isn't currently supported.`);
  process.exit(1)
}

const application = path.join(__dirname, 'publish', 'gpr.dll');

const args = [application].concat(process.argv.slice(2));
const child = spawn(command, args);

child.stdout.on('data', (data) => {
  process.stdout.write(data);
});

child.stderr.on('data', (data) => {
  process.stderr.write(data);
});

child.on('close', (code) => {
  process.exit(code)
});
