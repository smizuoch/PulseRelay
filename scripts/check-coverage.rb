#!/usr/bin/env ruby
# frozen_string_literal: true

require "rexml/document"

threshold = Float(ARGV.fetch(0, "95"))
coverage_glob = ARGV.fetch(1, "artifacts/coverage/**/coverage.cobertura.xml")
coverage_path = Dir[coverage_glob].max_by { |path| File.mtime(path) }

abort "No coverage file matched #{coverage_glob.inspect}." if coverage_path.nil?

document = REXML::Document.new(File.read(coverage_path))
line_rate = Float(document.root.attributes["line-rate"]) * 100.0

puts "Coverage file: #{coverage_path}"
puts format("Total line coverage: %.2f%%", line_rate)

if line_rate < threshold
  abort format("Line coverage %.2f%% is below required %.2f%%.", line_rate, threshold)
end
